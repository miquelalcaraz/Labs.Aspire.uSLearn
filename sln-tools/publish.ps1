param
(
    $deployToFeed = "false",
    $deployTolocal = "true",
    $NugetOfflinePackagesPath = "D:\.nuget\OfflinePackagesV2",
    $solutionFile = "",
    $solutionPath = ".."
)
# Variables
# Ruta al archivo de la solución
$feedName = "qltsystem"
$organization = "icstema"
$project = "QltSystem"
$source = "https://pkgs.dev.azure.com/$organization/_packaging/$feedName/nuget/v3/index.json"


#obtener la ruta relativa del archivo de la solución, a aprtir de la carpeta padre, si no se especifica
if ($solutionFile -eq "") {
    $solutionFile = Get-ChildItem -Path $solutionPath -Recurse -Filter "*.sln" | Select-Object -First 1
    if (-not $solutionFile) {
        Write-Host "No se encontró ningún archivo de solución en la carpeta actual."
        exit
    }
    $solutionFile = $solutionFile.FullName
}


# Leer el contenido del archivo de la solución
$solutionContent = Get-Content $solutionFile

# Filtrar las líneas que contienen información de los proyectos
$projectLines = $solutionContent | Where-Object { $_ -match '^Project\(' }

# Obtener el nombre de la rama actual
$branchName = git rev-parse --abbrev-ref HEAD


 
# Recorrer cada línea de proyecto y extraer la información
foreach ($line in $projectLines) {
    # Extraer el nombre del proyecto y la ruta relativa
    if ($line -match 'Project\(".*"\) = "(.*)", "(.*)", ".*"') {
        $projectName = $matches[1]
        $projectPath = $matches[2]

        # Convertir la ruta relativa a una ruta absoluta
        $absoluteProjectPath = Join-Path (Split-Path $solutionFile) $projectPath
        
        #si no existe un archivo de proyecto con extensión .csproj, se continua con el siguiente proyecto
        if (-not $projectPath.EndsWith(".csproj")) {
            continue
        }

        # Leer el contenido del archivo .csproj
        $csprojContent = Get-Content $absoluteProjectPath

        # Verificar si el archivo .csproj contiene la propiedad <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
        if (-not ($csprojContent -match '<GeneratePackageOnBuild>\s*true\s*</GeneratePackageOnBuild>')) {
            Write-Host "El proyecto $projectName no tiene la propiedad <GeneratePackageOnBuild>true. Se omite."
            continue
        }

        $outputPath = Join-Path (Split-Path $absoluteProjectPath) "bin\Release"
        # Si la carpeta de salida existe, eliminar su contenido
        if (Test-Path $outputPath) {
            Write-Host "Eliminando contenido de la carpeta de salida..."
            Remove-Item $outputPath -Recurse -Force
        }
        
        # Compilar el proyecto
        Write-Host "Compilando el proyecto..."
        dotnet build $absoluteProjectPath --configuration Release
 
        # Obtener la ruta del paquete generado con el filtro del nombre del proyecto
        # El proyecto tiene que tener la propiedad <GeneratePackageOnBuild>true</GeneratePackageOnBuild> en el archivo .csproj
        $packagePath = Get-ChildItem -Path $outputPath -Filter "*.nupkg" | Select-Object -Last 1

        if (-not $packagePath) {
            continue
        }
        
        Write-Host "Encontrado paquete NuGet generado para el proyecto $projectName."

        # Si deployToFeed es true o la rama es main publicar el paquete en Azure DevOps Artifacts
        if ($deployToFeed -eq "true" -and $branchName -eq "main") {
            # Publicar el paquete en Azure DevOps Artifacts
            Write-Host "Publicando el paquete en Azure DevOps Artifacts..."
            #az artifacts universal publish --organization "https://dev.azure.com/$organization" --feed $feedName --name $packagePath.Name --version "1.0.0" --path $packagePath.FullName
            #nuget push $packagePath.FullName -src https://pkgs.dev.azure.com/$organization/$project/_packaging/$feedName/nuget/v3/index.json -ApiKey az
            #dotnet nuget push --source https://pkgs.dev.azure.com/$organization/$project/_packaging/$feedName/nuget/v3/index.json  --api-key az $packagePath.FullName 
     
            dotnet nuget push --interactive --source $feedName --api-key az $packagePath.FullName 
                # Incrementar la versión del proyecto
            Write-Host "Incrementando la versión del proyecto..."
            #dotnet tool install --global nbgv
            nbgv tag

            # Hacer push de las etiquetas en el origen remoto
            Write-Host "Haciendo push de las etiquetas en el origen remoto..."
            git push --tags
        }
        # Copiar el paquete en la carpeta de paquetes offline de NuGet
        #Write-Host "Copiando el paquete en la carpeta de paquetes offline de NuGet..."
        if ($deployTolocal -eq "true" -or $branchName -eq "dev") {
            Copy-Item $packagePath.FullName $NugetOfflinePackagesPath
        }
        else {
            Write-Host "El paquete no se copiará en la carpeta de paquetes offline de NuGet."
        }
        if ($branchName -eq "main") {
            Write-Host "Incrementando la versión del proyecto para $projectName..."
            #Push-Location (Split-Path $absoluteProjectPath)
            #$tagName = "$projectName-$(nbgv get-version | Select-String -Pattern 'Version: (.*)' | ForEach-Object { $_.Matches.Groups[1].Value })"
            #nbgv tag -n $tagName

            nbgv tag -p $absoluteProjectPath

            #Pop-Location
            # Hacer push de las etiquetas en el origen remoto
            Write-Host "Haciendo push de las etiquetas en el origen remoto..."
            git push --tags
 
        }

    }
}
Write-Host "Proceso completado."
