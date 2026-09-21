# 检查文档归置.ps1 —— GerberParserV4.0 文档体系巡检
# ---------------------------------------------------------------------------
# 【只读】本脚本不修改、不移动、不删除任何文件，只打印检查报告。
#
# 用途：任何会话（人或 AI）产出 md 之后跑一次，检查是否有"漏置、缺段、未登记"。
# 规则来源：MD文件汇总（AI）/README.md 与 .workbuddy/memory/MEMORY.md。
# 规则来自桌面 RBLAOI 工程，2026-09-21 移植。
#
# 用法（在仓库根目录）：
#   powershell -ExecutionPolicy Bypass -File "MD文件汇总（AI）\检查文档归置.ps1"
# 或指定仓库根：
#   powershell -ExecutionPolicy Bypass -File "MD文件汇总（AI）\检查文档归置.ps1" -RepoRoot "D:\...\GerberParserV4.0"
# ---------------------------------------------------------------------------

param([string]$RepoRoot = '')

$ErrorActionPreference = 'Continue'

# ---------- 定位仓库根 ----------
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot  = Split-Path -Parent $scriptDir          # 脚本在 <repo>\MD文件汇总（AI）\ 下
}
$RepoRoot = $RepoRoot.TrimEnd('\') + '\'
if (-not (Test-Path $RepoRoot)) { Write-Host ('找不到仓库根: ' + $RepoRoot) -ForegroundColor Red; exit 2 }

$docsRoot = $RepoRoot + 'MD文件汇总（AI）\'
$problems = New-Object System.Collections.ArrayList
$infos    = New-Object System.Collections.ArrayList

function Add-Problem([string]$text) { [void]$problems.Add($text) }
function Add-Info([string]$text)    { [void]$infos.Add($text) }

# ---------- 1. 目录结构 ----------
$requiredDirs = @('待办', '任务&需求', '问题&解决方案', '系统架构&协议设计')
if (-not (Test-Path $docsRoot)) { Add-Problem '缺少目录: MD文件汇总（AI）/' }
else {
    foreach ($d in $requiredDirs) {
        if (-not (Test-Path ($docsRoot + $d))) { Add-Problem ('缺少目录: MD文件汇总（AI）/' + $d + '/') }
    }
    if (-not (Test-Path ($docsRoot + 'README.md')))  { Add-Problem '缺少索引入口: MD文件汇总（AI）/README.md' }
    if (-not (Test-Path ($docsRoot + '检查文档归置.ps1'))) { Add-Info '缺少巡检脚本: MD文件汇总（AI）/检查文档归置.ps1' }
}

# ---------- 2. 收集所有 md（排除构建产物、包、AI 过程记录） ----------
$exclude = '\\\.git\\|\\bin\\|\\obj\\|\\packages\\|\\\.workbuddy\\'
$allMd = @(Get-ChildItem -LiteralPath $RepoRoot -Recurse -Filter *.md -File -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -notmatch $exclude })

# 允许的位置：MD文件汇总（AI）/ 之下、仓库根的 README.md
function Get-AllowedArea([string]$full) {
    $rel = $full.Replace($RepoRoot, '')
    if ($rel -like 'MD文件汇总（AI）\*') { return 'docs' }
    if ($rel -eq 'README.md')            { return 'root-ok' }
    return 'other'
}

foreach ($f in $allMd) {
    $area = Get-AllowedArea $f.FullName
    $rel  = $f.FullName.Replace($RepoRoot, '')

    switch ($area) {
        'other' {
            Add-Problem ('【漏置】不在约定目录: ' + $rel + '  → 按内容归入 MD文件汇总（AI）/ 下的 待办 / 任务&需求 / 问题&解决方案 / 系统架构&协议设计')
        }
        'docs' {
            $txt = [System.IO.File]::ReadAllText($f.FullName)

            # 需求文档：必须含"验收目标"
            if ($rel -like 'MD文件汇总（AI）\任务&需求\R-*') {
                if ($txt -notmatch '验收目标') { Add-Problem ('【缺段】需求文档缺"验收目标": ' + $rel) }
                if ($txt -notmatch '状态')     { Add-Info    ('需求文档建议写"状态"行: ' + $rel) }
            }
            # 问题文档：必须含 根因 + 解决方案
            if ($rel -like 'MD文件汇总（AI）\问题&解决方案\*') {
                if ($rel -notlike '*README.md') {
                    if ($txt -notmatch '根因')     { Add-Problem ('【缺段】问题文档缺"根因": ' + $rel) }
                    if ($txt -notmatch '解决方案') { Add-Problem ('【缺段】问题文档缺"解决方案": ' + $rel) }
                    if ($f.BaseName -match '^\d')  { Add-Info ('问题文档命名建议以"问题点"开头、不加日期前缀: ' + $rel) }
                }
            }
            # 架构文档：建议标来源与核对日期
            if ($rel -like 'MD文件汇总（AI）\系统架构&协议设计\*') {
                if ($rel -notlike '*README.md') {
                    if ($txt -notmatch '来源')     { Add-Info ('架构文档建议标"（来源：文件:行号）": ' + $rel) }
                    if ($txt -notmatch '核对日期') { Add-Info ('架构文档建议写"最后核对日期": ' + $rel) }
                }
            }
            # 索引登记：MD文件汇总（AI）/README.md 里应提到该文件名
            $indexPath = $docsRoot + 'README.md'
            if (Test-Path $indexPath) {
                $index = [System.IO.File]::ReadAllText($indexPath)
                if ($rel -notlike '*README.md' -and $rel -ne 'MD文件汇总（AI）\README.md') {
                    if ($index -notmatch [regex]::Escape($f.Name)) {
                        Add-Problem ('【未登记】MD文件汇总（AI）/README.md 里没有登记: ' + $f.Name)
                    }
                }
            }
            # 路径写法：真正的"文档引用"（目录名 + xxx.md）应从仓库根写全
            if ($rel -ne 'MD文件汇总（AI）\README.md') {
                foreach ($k in @('`待办/', '`任务&需求/', '`问题&解决方案/', '`系统架构&协议设计/')) {
                    $pat = [regex]::Escape($k) + '[^`]*\.md'
                    if ($txt -match $pat) {
                        Add-Info ('文档引用未从仓库根写全（约定: MD文件汇总（AI）/xxx/yyy.md）: ' + $rel + '  含 ' + $k)
                    }
                }
            }
        }
    }
}

# ---------- 3. 输出 ----------
Write-Host ''
Write-Host '===== GerberParserV4.0 文档归置巡检（只读） =====' -ForegroundColor Cyan
Write-Host ('仓库根: ' + $RepoRoot)
Write-Host ('扫描 md 文件数: ' + $allMd.Count)
Write-Host ''
if ($problems.Count -eq 0) {
    Write-Host '[通过] 未发现结构性问题。' -ForegroundColor Green
} else {
    Write-Host ('[待处理] ' + $problems.Count + ' 项：') -ForegroundColor Yellow
    foreach ($p in $problems) { Write-Host ('  - ' + $p) -ForegroundColor Yellow }
}
if ($infos.Count -gt 0) {
    Write-Host ''
    Write-Host ('[提示] ' + $infos.Count + ' 项（不阻塞，供参考）：') -ForegroundColor DarkGray
    foreach ($i in $infos) { Write-Host ('  - ' + $i) -ForegroundColor DarkGray }
}
Write-Host ''
Write-Host '说明：本脚本只读。归置动作请人工确认后再做（AI 改动文件前应先说明）。' -ForegroundColor DarkGray
