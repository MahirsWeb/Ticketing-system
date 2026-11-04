<#
.SYNOPSIS
  Full backup of the Ticket System database, with a warning before SQL Server Express runs into its size limit.

.DESCRIPTION
  SQL Server Express has no SQL Server Agent, so it cannot schedule its own backups: run this from Windows Task Scheduler
  (see the README, "Backup"). It does three things:
    1. BACKUP DATABASE ... WITH CHECKSUM, then RESTORE VERIFYONLY on the file it just wrote;
    2. deletes this database's backups older than -KeepDays from the backup folder (only when this computer can see that folder);
    3. compares the size of the data files with the limit (Express: 10 GB per database) and warns from -WarnAt (default 70 %) upwards.
  Exit code: 0 = all well, 1 = the backup or its check failed, 2 = the backup is fine but the database is getting close to the limit.

  The backup folder is read and written by the SQL Server SERVICE account, not by you: make sure that account may write there.
  Without -BackupFolder the instance's own default backup folder is used, which it can always write to.

  A copy of the database is not enough to restore the application. Keep the key ring certificate (DataProtection:CertificatePath)
  and the attachments folder (Storage:UploadsPath) with it, in a separate place from the database backup (README, "Backup").

.EXAMPLE
  .\backup-database.ps1 -ConnectionString "Server=localhost\SQLEXPRESS;Database=TicketSystemTech;Trusted_Connection=True;TrustServerCertificate=True"
#>
param(
  [Parameter(Mandatory = $true)] [string] $ConnectionString,
  [string] $BackupFolder,
  [int] $KeepDays = 14,
  [double] $LimitGb = 10,
  [double] $WarnAt = 0.7
)

$ErrorActionPreference = 'Stop'

function Invoke-Scalar($connection, [string] $text) {
  $command = $connection.CreateCommand()
  $command.CommandText = $text
  $command.CommandTimeout = 0   # a backup of a large database takes as long as it takes
  return $command.ExecuteScalar()
}

$connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
$exitCode = 0
try {
  $connection.Open()
  $database = [string](Invoke-Scalar $connection 'SELECT DB_NAME()')
  if (-not $BackupFolder) { $BackupFolder = [string](Invoke-Scalar $connection "SELECT CONVERT(nvarchar(512), SERVERPROPERTY('InstanceDefaultBackupPath'))") }
  if (-not $BackupFolder) { throw 'The instance has no default backup folder; pass -BackupFolder.' }

  $stamp = (Get-Date).ToString('yyyyMMdd_HHmmss')
  $file = Join-Path $BackupFolder "${database}_$stamp.bak"
  $escapedFile = $file.Replace("'", "''")
  $escapedDatabase = $database.Replace(']', ']]')

  Write-Output "Backing up [$database] to $file"
  [void](Invoke-Scalar $connection "BACKUP DATABASE [$escapedDatabase] TO DISK = N'$escapedFile' WITH CHECKSUM, INIT, FORMAT, NAME = N'$stamp'")
  [void](Invoke-Scalar $connection "RESTORE VERIFYONLY FROM DISK = N'$escapedFile' WITH CHECKSUM")
  Write-Output 'The backup was written and verified.'

  # Retention. The backup above is already safe, so a problem here (the database is on another server, or the folder belongs to
  # the SQL Server service account and this account may not list it) is a warning, never a failed backup.
  try {
    if (-not (Test-Path -LiteralPath $BackupFolder)) { throw "$BackupFolder is not visible from this computer" }
    $old = Get-ChildItem -LiteralPath $BackupFolder -Filter "${database}_*.bak" |
      Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$KeepDays) }
    foreach ($f in $old) { Remove-Item -LiteralPath $f.FullName; Write-Output "Removed $($f.Name) (older than $KeepDays days)" }
  }
  catch {
    Write-Warning "Old backups were not cleaned up: $($_.Exception.Message). Clean $BackupFolder some other way (a second scheduled task run by an account that may)."
  }

  # Size of the data files (the log does not count towards the Express limit).
  $dataGb = [double](Invoke-Scalar $connection 'SELECT CONVERT(float, SUM(CONVERT(bigint, size))) * 8 / 1024 / 1024 FROM sys.database_files WHERE type = 0')
  $share = $dataGb / $LimitGb
  Write-Output ('Data files: {0:N2} GB of {1:N0} GB ({2:P0})' -f $dataGb, $LimitGb, $share)
  if ($share -ge $WarnAt) {
    Write-Warning ('The database is at {0:P0} of the {1:N0} GB limit. Plan a move to SQL Server Standard before it is reached.' -f $share, $LimitGb)
    $exitCode = 2
  }
}
catch {
  Write-Error ("Backup failed: " + $_.Exception.Message)
  $exitCode = 1
}
finally {
  $connection.Dispose()
}
exit $exitCode
