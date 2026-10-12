# MomoiProject — 프로젝트 코드 컴파일 검사(Claude Code의 Stop 훅이 응답을 끝낼 때 부른다). Unity를 연 채로도 된다.
# Unity가 만든 .csproj(Preferences › External Tools의 프로젝트 파일)의 참조·심볼을 빌려, Assets 아래 asmdef마다 그 폴더의 .cs를 그대로 컴파일한다
# (방금 만든 파일도 들어간다). asmdef 밖의 코드는 Assembly-CSharp(·Editor 폴더는 Assembly-CSharp-Editor)로.
# UXML 파일이 XML로 읽히는지도 본다(읽히지 않으면 Unity가 가져오지 못해 화면이 빈 채로 열린다).
# 오류가 있으면 stderr에 쓰고 2로 끝난다 → Claude가 고친 뒤 다시 끝낸다. 같은 이유로 세 번 넘게 막지는 않는다.
# 검사할 수 없으면(Unity·컴파일러·csproj가 없음) 알리고 0으로 끝난다(막지 않는다).
# 시작 설정이 GBible 패키지의 Tools~/compile-check.ps1을 .claude/hooks/로 복사한다(직접 고치지 않는다).
# 손으로: powershell -NoProfile -ExecutionPolicy Bypass -File .claude/hooks/compile-check.ps1 -Always
param([switch]$Always)
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$proj = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
$proj = $proj.TrimEnd('\', '/')
$state = Join-Path $proj "Logs\MomoiProject"
New-Item -ItemType Directory -Force $state | Out-Null
$stamp = Join-Path $state "compile-check.stamp"
$blocks = Join-Path $state "compile-check.blocks"

# 훅 입력(JSON)의 stop_hook_active: 이 훅이 막아서 Claude가 이어서 일하는 중이면 참.
$hookActive = $false
try {
    if ([Console]::IsInputRedirected) {
        $raw = [Console]::In.ReadToEnd()
        if ($raw) { $hookActive = [bool](($raw | ConvertFrom-Json).stop_hook_active) }
    }
} catch { }

$assets = Join-Path $proj "Assets"
if (-not (Test-Path $assets)) { exit 0 }

# 막기: 오류를 stderr에 쓰고 2로 끝낸다(같은 이유로 세 번 넘게는 막지 않는다).
function Stop-WithErrors($list, $what) {
    $count = 1
    if (Test-Path $blocks) { try { $count = [int](Get-Content $blocks -Raw) + 1 } catch { $count = 1 } }
    Set-Content $blocks $count
    if ($hookActive -and $count -gt 3) {
        "MomoiProject 컴파일 검사: $what $($list.Count)개가 남아 있지만 세 번 막아서 더 막지 않습니다. 보고에 남은 오류를 적으세요."
        $list | Select-Object -First 20
        exit 0
    }
    [Console]::Error.WriteLine("MomoiProject 컴파일 검사: $what $($list.Count)개. 고친 뒤 다시 끝내세요.")
    $list | Select-Object -First 30 | ForEach-Object { [Console]::Error.WriteLine($_) }
    exit 2
}

# UXML: XML로 읽히지 않으면 Unity가 가져오지 못해 그 화면이 빈 채로 열린다(콘솔에는 가져올 때 한 번만 남는다) — 늘 본다(빠르다).
# 흔한 까닭은 주석 안의 '--'(XML이 막는다): 변형 클래스는 주석에 'kit-hub +aside'처럼 적는다.
$uxmlErrors = New-Object System.Collections.Generic.List[string]
$uxmlFiles = @(Get-ChildItem $assets -Recurse -File -Filter *.uxml -ErrorAction SilentlyContinue)
foreach ($p in @(Get-ChildItem (Join-Path $proj "Packages") -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName "package.json") })) {
    $uxmlFiles += @(Get-ChildItem $p.FullName -Recurse -File -Filter *.uxml -ErrorAction SilentlyContinue)
}
foreach ($u in $uxmlFiles) {
    try { (New-Object System.Xml.XmlDocument).Load($u.FullName) }
    catch {
        $e = $_.Exception
        while ($e.InnerException) { $e = $e.InnerException }
        $hint = if ($e.Message -match 'comment') { " (주석 안의 '--'는 쓸 수 없습니다 — 'kit-hub +aside'처럼)" } else { "" }
        $uxmlErrors.Add(($u.FullName -replace [regex]::Escape("$proj\"), '') + ": UXML이 XML로 읽히지 않습니다 — " + $e.Message + $hint)
    }
}
if ($uxmlErrors.Count -gt 0) { Stop-WithErrors $uxmlErrors "UXML 오류" }

# 마지막으로 통과한 뒤 바뀐 .cs·.asmdef가 없으면 끝.
# 폴더의 시각도 본다: 파일을 지우거나 옮기면 그 폴더의 시각이 바뀐다(지운 클래스를 쓰던 코드도 잡는다).
$files = @(Get-ChildItem $assets -Recurse -File -Include *.cs, *.asmdef -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) { exit 0 }
$folders = @(Get-ChildItem $assets -Recurse -Directory -ErrorAction SilentlyContinue) + @(Get-Item $assets)
# 프로젝트 안의 패키지(Packages/<이름>/package.json — GBible처럼 고쳐 쓰는 패키지)도 본다: 패키지 코드를 고친 뒤 Unity가 아직 다시 컴파일하지 않았으면
# Library의 옛 DLL로 검사해 없는 오류를 내므로, 소스가 그 DLL보다 새로우면 패키지 어셈블리도 여기서 컴파일한다(~ 폴더 — 문서·도구 — 는 Unity처럼 뺀다).
$pkgRoots = @(Get-ChildItem (Join-Path $proj "Packages") -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName "package.json") })
foreach ($p in $pkgRoots) {
    $files += @(Get-ChildItem $p.FullName -Recurse -File -Include *.cs, *.asmdef -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '~\\' })
    $folders += @(Get-ChildItem $p.FullName -Recurse -Directory -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '~(\\|$)' })
}
$latest = (@($files) + $folders | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
if (-not $Always -and (Test-Path $stamp) -and $latest -le (Get-Item $stamp).LastWriteTimeUtc) {
    Remove-Item $blocks -ErrorAction SilentlyContinue
    exit 0
}

# Unity(프로젝트 버전)와 컴파일러.
$versionFile = Join-Path $proj "ProjectSettings\ProjectVersion.txt"
$match = if (Test-Path $versionFile) { Select-String $versionFile -Pattern '^m_EditorVersion: (.+)$' } else { $null }
if (-not $match) { "MomoiProject 컴파일 검사: ProjectVersion.txt를 읽지 못해 건너뜁니다."; exit 0 }
$ver = $match.Matches[0].Groups[1].Value.Trim()
$roots = @("$env:ProgramFiles\Unity\Hub\Editor", "C:\Program Files\Unity\Hub\Editor")
$secondary = Join-Path $env:APPDATA "UnityHub\secondaryInstallPath.json"
if (Test-Path $secondary) { try { $p = Get-Content $secondary -Raw | ConvertFrom-Json; if ($p) { $roots += "$p" } } catch { } }
$unity = $roots | ForEach-Object { Join-Path $_ "$ver\Editor\Data" } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $unity -and $env:MOMOI_UNITY_EDITOR_DATA) { $unity = $env:MOMOI_UNITY_EDITOR_DATA }
if (-not $unity) { "MomoiProject 컴파일 검사: Unity $ver 설치 폴더를 찾지 못해 건너뜁니다(환경 변수 MOMOI_UNITY_EDITOR_DATA에 …\Editor\Data를 넣으면 쓴다)."; exit 0 }
$dotnet = Join-Path $unity "NetCoreRuntime\dotnet.exe"
$csc = Get-ChildItem (Join-Path $unity "DotNetSdkRoslyn"), (Join-Path $unity "DotNetSdk") -Recurse -Filter csc.dll -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
if (-not (Test-Path $dotnet) -or -not $csc) { "MomoiProject 컴파일 검사: Unity의 C# 컴파일러를 찾지 못해 건너뜁니다."; exit 0 }

# asmdef: 이름 → 폴더·참조. GUID 참조도 읽는다.
$infos = @{}
$guidToName = @{}
$asmdefFiles = @(Get-ChildItem $assets -Recurse -Filter *.asmdef -File)
foreach ($p in $pkgRoots) { $asmdefFiles += @(Get-ChildItem $p.FullName -Recurse -Filter *.asmdef -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '~\\' }) }
$pkgPrefix = Join-Path $proj "Packages\"
foreach ($a in $asmdefFiles) {
    try { $json = Get-Content $a.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } catch { continue }
    if (-not $json.name) { continue }
    $meta = "$($a.FullName).meta"
    if (Test-Path $meta) { $g = (Select-String $meta -Pattern '^guid: (\w+)').Matches; if ($g.Count -gt 0) { $guidToName[$g[0].Groups[1].Value] = $json.name } }
    $infos[$json.name] = @{ Dir = $a.DirectoryName.TrimEnd('\') + '\'; Refs = @($json.references); Package = $a.FullName.StartsWith($pkgPrefix) }
}
$asmDirs = @($infos.Values | ForEach-Object { $_.Dir })

function Get-Sources($dir) {
    $nested = @($asmDirs | Where-Object { $_ -ne $dir -and $_.StartsWith($dir) })
    @(Get-ChildItem $dir -Recurse -Filter *.cs -File | Where-Object { $f = $_.FullName; -not ($nested | Where-Object { $f.StartsWith($_) }) } | ForEach-Object FullName)
}

# 참조 순서(앞의 것을 먼저 컴파일해 뒤의 것이 그 결과를 쓴다).
$order = New-Object System.Collections.Generic.List[string]
$visiting = @{}
function Visit($n) {
    if ($order.Contains($n) -or $visiting[$n]) { return }
    $visiting[$n] = $true
    foreach ($r in $infos[$n].Refs) {
        $rn = if ("$r".StartsWith("GUID:")) { $guidToName["$r".Substring(5)] } else { "$r" }
        if ($rn -and $infos.ContainsKey($rn)) { Visit $rn }
    }
    $order.Add($n)
}
foreach ($n in @($infos.Keys | Sort-Object)) { Visit $n }

$out = Join-Path $state "build"
New-Item -ItemType Directory -Force $out | Out-Null
$errors = New-Object System.Collections.Generic.List[string]
$skipped = New-Object System.Collections.Generic.List[string]
$script:checked = 0
$rebuilt = New-Object System.Collections.Generic.List[string]

function Invoke-Compile($name, $src, $package = $false) {
    if ($src.Count -eq 0) { return }
    $csproj = Join-Path $proj "$name.csproj"
    # 패키지 어셈블리는 csproj가 없으면(꺼진 선택 기능 등) 조용히 Library의 DLL을 쓴다.
    if (-not (Test-Path $csproj)) { if (-not $package) { $skipped.Add($name) }; return }
    [xml]$x = Get-Content $csproj -Raw -Encoding UTF8
    $deps = @($x.Project.ItemGroup.ProjectReference | Where-Object { $_ } | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Include) })
    # 소스·csproj가 지난번 결과보다 오래되었고 다시 만든 참조가 없으면 지난 결과를 쓴다.
    $dll = Join-Path $out "$name.dll"
    $newest = (@($src | ForEach-Object { Get-Item -LiteralPath $_ }) + @(Get-Item -LiteralPath $csproj) | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
    if ((Test-Path $dll) -and (Get-Item $dll).LastWriteTimeUtc -ge $newest -and -not ($deps | Where-Object { $rebuilt.Contains($_) })) { return }
    # 패키지 어셈블리: Unity가 이미 지금 소스로 컴파일했으면(Library의 DLL이 더 새로움) 그것을 쓴다.
    $library = Join-Path $proj "Library\ScriptAssemblies\$name.dll"
    if ($package -and (Test-Path $library) -and (Get-Item $library).LastWriteTimeUtc -ge $newest -and -not ($deps | Where-Object { $rebuilt.Contains($_) })) { return }
    $defines = ((@($x.Project.PropertyGroup.DefineConstants) -join ';').Split(';') | Where-Object { $_ }) -join ';'
    $lang = @($x.Project.PropertyGroup.LangVersion) | Where-Object { $_ } | Select-Object -First 1
    if (-not $lang) { $lang = "9.0" }
    $refs = @($x.Project.ItemGroup.Reference | Where-Object { $_ -and $_.HintPath } | ForEach-Object {
        $p = $_.HintPath; if (-not [IO.Path]::IsPathRooted($p)) { $p = Join-Path $proj $p }; $p })
    foreach ($pr in @($x.Project.ItemGroup.ProjectReference | Where-Object { $_ })) {
        $rn = [IO.Path]::GetFileNameWithoutExtension($pr.Include)
        # 여기서 만든 것과 Library의 것 중 더 새로운 쪽(패키지를 Unity가 다시 컴파일했으면 Library 쪽이 새롭다).
        $built = Join-Path $out "$rn.dll"
        $library = Join-Path $proj "Library\ScriptAssemblies\$rn.dll"
        if ((Test-Path $built) -and (-not (Test-Path $library) -or (Get-Item $built).LastWriteTimeUtc -gt (Get-Item $library).LastWriteTimeUtc)) { $refs += $built } else { $refs += $library }
    }
    $refs = @($refs | Where-Object { Test-Path $_ } | Sort-Object -Unique)
    $rsp = Join-Path $out "$name.rsp"
    $lines = @("-nologo", "-nostdlib+", "-noconfig", "-target:library", "-langversion:$lang", "-nowarn:0169,1701,1702",
               "-out:$(Join-Path $out "$name.dll")", "-define:$defines") + ($refs | ForEach-Object { "-r:`"$_`"" }) + ($src | ForEach-Object { "`"$_`"" })
    [IO.File]::WriteAllLines($rsp, $lines)
    $o = & $dotnet $csc "@$rsp" 2>&1 | ForEach-Object { "$_" }
    $script:checked++
    $rebuilt.Add($name)
    if ($LASTEXITCODE -ne 0) {
        foreach ($line in ($o | Where-Object { $_ -match 'error CS' } | Sort-Object -Unique)) { $errors.Add(($line -replace [regex]::Escape("$proj\"), '')) }
    }
}

foreach ($n in $order) { Invoke-Compile $n (Get-Sources $infos[$n].Dir) $infos[$n].Package }
# asmdef 밖의 코드: Plugins·Standard Assets → firstpass, Editor 폴더 → …-Editor(Unity의 미리 정한 어셈블리와 같은 나눔·순서).
$loose = @(Get-ChildItem $assets -Recurse -Filter *.cs -File | Where-Object { $f = $_.FullName; -not ($asmDirs | Where-Object { $f.StartsWith($_) }) } | ForEach-Object FullName)
$firstRoots = @((Join-Path $assets "Plugins\"), (Join-Path $assets "Standard Assets\"))
$isFirst = { param($f) [bool]($firstRoots | Where-Object { $f.StartsWith($_) }) }
$isEditor = { param($f) $f -match '\\Editor\\' }
Invoke-Compile "Assembly-CSharp-firstpass" @($loose | Where-Object { (& $isFirst $_) -and -not (& $isEditor $_) })
Invoke-Compile "Assembly-CSharp-Editor-firstpass" @($loose | Where-Object { (& $isFirst $_) -and (& $isEditor $_) })
Invoke-Compile "Assembly-CSharp" @($loose | Where-Object { -not (& $isFirst $_) -and -not (& $isEditor $_) })
Invoke-Compile "Assembly-CSharp-Editor" @($loose | Where-Object { -not (& $isFirst $_) -and (& $isEditor $_) })

if ($errors.Count -eq 0) {
    Set-Content $stamp (Get-Date).ToString("o")
    Remove-Item $blocks -ErrorAction SilentlyContinue
    if ($skipped.Count -gt 0) { "MomoiProject 컴파일 검사: 통과. csproj가 없어 건너뛴 어셈블리: $($skipped -join ', ') — Unity가 한 번 컴파일하면(또는 Preferences › External Tools › Regenerate project files) 다음부터 검사된다." }
    exit 0
}

Stop-WithErrors $errors "컴파일 오류"
