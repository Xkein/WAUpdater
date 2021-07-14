

$indir = "LocalWAGame"
$outdir = "RemoteWAGame"
$VersionNumber = "1.13"

$UserName = "enter server user name"
$UserPassword = "enter server user password"


function CleanUp($dir)
{
    echo "Cleaning up $dir"
    if(Test-Path $dir){
        [System.IO.Directory]::Delete($dir, $true)
    }
}

$FTP_URIBase = "enter FTP URI base"
function SendFile($file_name)
{
    $package = New-Object System.Io.FileInfo($file_name)
    $upFTP = [System.Net.FtpWebRequest] [System.Net.WebRequest]::Create("$FTP_URIBase/$file_name")
    $upFTP.Credentials = New-Object System.Net.NetworkCredential($UserName, $UserPassword)
    $upFTP.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
    $upFTP.KeepAlive = $true
    $sourceStream = [System.IO.File]::OpenRead($package.FullName)
    $upFTP.ContentLength = $sourceStream.Length
    $requestStream = $upFTP.GetRequestStream()
    $sourceStream.CopyTo($requestStream)
    $requestStream.Close()
    $sourceStream.Close()
    $response = $upFTP.GetResponse()
    $response.StatusDescription
    $response.Close()
}

CleanUp $outdir

cd $indir
[System.IO.Directory]::SetCurrentDirectory($pwd)

Import-Module -Name .\WAUpdater.dll

$Mirrors = @{
    gitee = New-Object WAUpdater.UpdateMirror('gitee', 'enter Mirror URI base', 'China', 1MB);
    xkein = New-Object WAUpdater.UpdateMirror("Xkein's Server", "enter Mirror URI base", "China")
}

$Mirror = $Mirrors['xkein']
echo "select $Mirror in $Mirrors"
$Updater = New-Object WAUpdater.Updater($Mirror)
$VersionFile = $Updater.VersionFile
echo 'calculate checksums...'
$VersionFile.Calculate($Updater.Ignore, $Updater.Decomposer)
echo 'writing checksums...'
$VersionFile.VersionNumber = $VersionNumber
$VersionFile.Write();

$fileList = $VersionFile.GetUploadableFiles()
#$fileList | foreach {"$indir\$_"} | Tee-Object -Variable newList
#$fileList = $newList

cd ..
[System.IO.Directory]::SetCurrentDirectory($pwd)

echo "uploading..."
if ($Mirror -eq $Mirrors['gitee'])
{
    $fileList | foreach {
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName("$outdir\$_"))
        copy "$indir\$_" "$outdir\$_"
    }
    cd $outdir

    git init
    git remote add origin git@gitee.com:xkein/test.git
    git add version_file
    # git add -A -- $fileList
    $fileList | ForEach-Object { git add $_ }

    git commit -m @("{0}¸üÐÂ" -f (Get-Date))
    git push -f -u origin master

}
if ($Mirror -eq $Mirrors['xkein'])
{
    $fileList | foreach {
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName("$outdir\$_"))
        copy "$indir\$_" "$outdir\$_"
    }
    copy $indir\version_file $outdir\version_file

    echo "compressing..."
    $package_name = "WA_$(Get-Date -Format 'yyyy-MM-dd-HH-mm').zip"
    Compress-Archive -Force -Path "$outdir\*" -DestinationPath $package_name

    echo "send package by FTP."
    SendFile $package_name
    
    echo "send Expand Action."
    Write-Output "no actual content" >> Expand_Action
    SendFile Expand_Action
    rm Expand_Action
}

pause