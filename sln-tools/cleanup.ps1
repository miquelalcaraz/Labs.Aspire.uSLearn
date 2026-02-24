param
(
  $sourcesPath = "."
)
 
function main () {
  CleanResultsFolder
  CleanEmptyFolders  
}

function CleanEmptyFolders () {
  
  $dirs = Get-ChildItem $sourcesPath -directory -recurse | 
  Where-Object { 
    (Get-ChildItem $_.fullName).count -eq 0 
  } | 
  Select-Object -expandproperty FullName

  $dirs | Foreach-Object { 
    $message = "removing $($_)"
    Write-Host $message 
    Remove-Item $_  -Force
    "$($message) -> OK"
  } 
}

function CleanResultsFolder () {
    
  $dirs = Get-ChildItem $sourcesPath -include bin, obj -directory -recurse | 
  Select-Object FullName

  $dirs | Foreach-Object { 
    $message = "removing $($_.FullName)"
    Write-Host $message 
    Remove-Item $_.FullName -recurse -Force
    "$($message) -> OK" 
  } 
}

main
 