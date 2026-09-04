Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$proc = Get-Process -Name "PChabit" -ErrorAction SilentlyContinue
if (-not $proc) {
    Write-Output "PChabit is not running!"
    exit 1
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)

# Find all ListItem elements
$listItemCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$allListItems = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listItemCondition)

Write-Output "Total ListItem elements: $($allListItems.Count)"

# Test order: AppStats(2), DataManagement(6), Goals(7), Settings(8), WebAccess(4)
# Test WebAccess LAST to see if other pages work first
$testOrder = @(2, 6, 7, 8, 4)

foreach ($idx in $testOrder) {
    if ($idx -ge $allListItems.Count) {
        Write-Output "Index $idx out of range"
        continue
    }

    # Re-find elements (they might be invalidated after navigation)
    $allListItems = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listItemCondition)
    if ($idx -ge $allListItems.Count) {
        Write-Output "Index $idx out of range after re-find"
        continue
    }

    $item = $allListItems[$idx]
    $name = $item.Current.Name
    Write-Output ""
    Write-Output "=== Testing index $idx ($name) ==="

    $clicked = $false
    try {
        $selectPattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        if ($selectPattern) {
            $selectPattern.Select()
            $clicked = $true
            Write-Output "  Selected"
        }
    } catch {}

    if (-not $clicked) {
        try {
            $invokePattern = $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            if ($invokePattern) {
                $invokePattern.Invoke()
                $clicked = $true
                Write-Output "  Invoked"
            }
        } catch {}
    }

    if (-not $clicked) {
        Write-Output "  FAILED to click"
        continue
    }

    Start-Sleep -Seconds 5

    $procAfter = Get-Process -Name "PChabit" -ErrorAction SilentlyContinue
    if ($procAfter) {
        Write-Output "  SUCCESS: App running"
    } else {
        Write-Output "  FAILURE: App crashed!"
        exit 1
    }
}

Write-Output ""
Write-Output "=== Final result ==="
$finalProc = Get-Process -Name "PChabit" -ErrorAction SilentlyContinue
if ($finalProc) {
    Write-Output "App is still running"
} else {
    Write-Output "App is NOT running"
}
