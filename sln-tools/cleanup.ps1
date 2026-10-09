param
(
  $sourcesPath = "."
)
 
function main () {
  CleanResultsFolder
  CleanEmptyFolders  
}

function CleanEmptyFolders () {
  
  # Deepest folders first, so parents emptied by the removal of their children are removed too
  $dirs = Get-ChildItem $sourcesPath -directory -recurse -force |
  Sort-Object { $_.FullName.Length } -Descending |
  Select-Object -expandproperty FullName

  $dirs | Foreach-Object {
    if ((Get-ChildItem $_ -force).count -eq 0) {
      $message = "removing $($_)"
      Write-Host $message
      Remove-Item $_ -Force
      "$($message) -> OK"
    }
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
 