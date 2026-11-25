# تثبيت/تحديث برنامج المحل بضغطة واحدة.
# يبني آخر نسخة من الكود، ينسخها إلى مجلد البرنامج ShopManager-new دون أن يمسّ بياناتك،
# يأخذ نسخة احتياطية من shop.db قبل التحديث، ويضع اختصاراً واحداً على سطح المكتب.
param([switch]$Quiet)   # -Quiet: بدون نوافذ رسائل (للتشغيل الآلي)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

function Show-Msg([string]$text, [string]$icon = 'Information') {
    if ($Quiet) { Write-Host "[$icon] $text"; return }
    [System.Windows.Forms.MessageBox]::Show($text, 'برنامج المحل', 'OK', $icon,
        'Button1', [System.Windows.Forms.MessageBoxOptions]::RtlReading) | Out-Null
}

$src  = $PSScriptRoot                                   # مجلد الكود
$root = Split-Path $src                                 # المجلد M
$dest = Join-Path $root 'ShopManager-new'               # مجلد البرنامج الذي تعمل عليه
$tmp  = Join-Path $env:TEMP 'ShopManager_publish'

try {
    # 1) البرنامج يجب أن يكون مغلقاً، وإلا لا يمكن استبدال ملفاته
    $running = Get-Process ShopManager -ErrorAction SilentlyContinue
    if ($running) {
        Show-Msg "البرنامج مفتوح الآن.`nأغلقه أولاً (من زر الإغلاق داخل البرنامج) ثم شغّل التحديث مرة ثانية." 'Warning'
        exit 1
    }

    # 2) بناء آخر نسخة
    Write-Host 'Building...'
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    & dotnet publish (Join-Path $src 'ShopManager.csproj') -c Release -r win-x64 --self-contained false -o $tmp --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'فشل بناء البرنامج (dotnet publish). لم يتغيّر شيء في مجلد البرنامج.' }

    # 3) نسخة احتياطية من البيانات قبل التحديث
    New-Item -ItemType Directory -Force $dest | Out-Null
    $db = Join-Path $dest 'shop.db'
    $backupNote = ''
    if (Test-Path $db) {
        $bdir = Join-Path $dest 'backups'
        New-Item -ItemType Directory -Force $bdir | Out-Null
        $bfile = Join-Path $bdir ('shop_before_update_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.db')
        Copy-Item $db $bfile
        $backupNote = "`nنسخة احتياطية من بياناتك: backups\" + (Split-Path $bfile -Leaf)
    }

    # 4) نسخ ملفات البرنامج فقط؛ البيانات وإعدادات المزامنة وملفات الاستيراد لا تُستبدل أبداً
    Write-Host 'Copying...'
    & robocopy $tmp $dest /E /NFL /NDL /NJH /NJS /NP `
        /XF shop.db sync_config.json sync_state.json errors.log *.csv /XD backups | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "فشل نسخ الملفات (robocopy $LASTEXITCODE)." }
    # ملفات الاستيراد تُنسخ فقط إن لم تكن موجودة
    Get-ChildItem $tmp -Filter *.csv | ForEach-Object {
        $t = Join-Path $dest $_.Name
        if (-not (Test-Path $t)) { Copy-Item $_.FullName $t }
    }

    # 5) إبعاد النسخة القديمة الأصلية من المجلد M حتى لا تُفتح بالخطأ (نقل وليس حذف)
    $oldExe = Join-Path $root 'ShopManager.exe'
    if (Test-Path $oldExe) {
        $old = Join-Path $root 'old_version_do_not_use'
        New-Item -ItemType Directory -Force $old | Out-Null
        Get-ChildItem $root -File | Move-Item -Destination $old -Force
    }

    # 6) اختصار واحد على سطح المكتب يفتح النسخة الصحيحة دائماً
    $exe = Join-Path $dest 'ShopManager.exe'
    $desktop = [Environment]::GetFolderPath('Desktop')
    $ws = New-Object -ComObject WScript.Shell
    # WScript.Shell لا يحفظ اسماً عربياً مباشرة: نحفظه باسم إنجليزي مؤقت ثم نعيد التسمية
    $tmpLnk = Join-Path $env:TEMP 'ShopManager_shortcut.lnk'
    $lnk = $ws.CreateShortcut($tmpLnk)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $dest
    $lnk.IconLocation = "$exe,0"
    $lnk.Save()
    Move-Item $tmpLnk (Join-Path $desktop 'برنامج المحل.lnk') -Force

    $ver = (Get-Item (Join-Path $dest 'ShopManager.dll')).LastWriteTime.ToString('yyyy-MM-dd HH:mm')
    Show-Msg ("تم التحديث بنجاح.`nافتح البرنامج من أيقونة (برنامج المحل) على سطح المكتب.`nتاريخ النسخة: $ver" + $backupNote)
}
catch {
    Show-Msg ("لم يكتمل التحديث:`n" + $_.Exception.Message) 'Error'
    exit 1
}
