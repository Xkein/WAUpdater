
$indir = "enter FTP location"
$outdir = "enter Mirror URI"
$watcher = New-Object System.IO.FileSystemWatcher($indir, "*")
$watcher.IncludeSubdirectories = $true
while($true)
{
    $result = $watcher.WaitForChanged([System.IO.WatcherChangeTypes]::All)
    if ($result.Name.Contains("Expand_Action"))
    {
        $Archives = [System.IO.Directory]::GetFiles($indir, "WA_*.zip")
        $Archive = $Archives[$Archives.Length - 1]

        rm -Recurse $outdir
        Expand-Archive -Path $Archive -DestinationPath $outdir
        rm $indir\Expand_Action
    }
    if ($result.Name.Contains("Copy_Action"))
    {
        rm -Recurse $outdir
        copy -Recurse $indir\game $outdir
        rm $indir\Copy_Action
    }
}