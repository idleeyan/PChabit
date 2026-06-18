import os, json, sys
from xml.etree import ElementTree as ET

PROJECT_DIR = r"E:\SYNC\My VS\PChabit\src\PChabit.App"
OBJ_DIR = os.path.join(PROJECT_DIR, "obj", "Release", "net9.0-windows10.0.22621.0", "win-x64")

# XAML element to WinUI type mapping (sufficient for x:Name field types)
TYPE_MAP = {
    'Application': 'global::Microsoft.UI.Xaml.Application',
    'Page': 'global::Microsoft.UI.Xaml.Controls.Page',
    'UserControl': 'global::Microsoft.UI.Xaml.Controls.UserControl',
    'ContentDialog': 'global::Microsoft.UI.Xaml.Controls.ContentDialog',
    'TextBlock': 'global::Microsoft.UI.Xaml.Controls.TextBlock',
    'Button': 'global::Microsoft.UI.Xaml.Controls.Button',
    'HyperlinkButton': 'global::Microsoft.UI.Xaml.Controls.HyperlinkButton',
    'ToggleButton': 'global::Microsoft.UI.Xaml.Controls.ToggleButton',
    'RepeatButton': 'global::Microsoft.UI.Xaml.Controls.RepeatButton',
    'CheckBox': 'global::Microsoft.UI.Xaml.Controls.CheckBox',
    'RadioButton': 'global::Microsoft.UI.Xaml.Controls.RadioButton',
    'ToggleSwitch': 'global::Microsoft.UI.Xaml.Controls.ToggleSwitch',
    'ComboBox': 'global::Microsoft.UI.Xaml.Controls.ComboBox',
    'NumberBox': 'global::Microsoft.UI.Xaml.Controls.NumberBox',
    'TextBox': 'global::Microsoft.UI.Xaml.Controls.TextBox',
    'PasswordBox': 'global::Microsoft.UI.Xaml.Controls.PasswordBox',
    'RichEditBox': 'global::Microsoft.UI.Xaml.Controls.RichEditBox',
    'Slider': 'global::Microsoft.UI.Xaml.Controls.Slider',
    'DatePicker': 'global::Microsoft.UI.Xaml.Controls.DatePicker',
    'CalendarDatePicker': 'global::Microsoft.UI.Xaml.Controls.CalendarDatePicker',
    'TimePicker': 'global::Microsoft.UI.Xaml.Controls.TimePicker',
    'ListView': 'global::Microsoft.UI.Xaml.Controls.ListView',
    'GridView': 'global::Microsoft.UI.Xaml.Controls.GridView',
    'ItemsControl': 'global::Microsoft.UI.Xaml.Controls.ItemsControl',
    'ItemsRepeater': 'global::Microsoft.UI.Xaml.Controls.ItemsRepeater',
    'FlipView': 'global::Microsoft.UI.Xaml.Controls.FlipView',
    'TreeView': 'global::Microsoft.UI.Xaml.Controls.TreeView',
    'NavigationView': 'global::Microsoft.UI.Xaml.Controls.NavigationView',
    'TabView': 'global::Microsoft.UI.Xaml.Controls.TabView',
    'Pivot': 'global::Microsoft.UI.Xaml.Controls.Pivot',
    'CommandBar': 'global::Microsoft.UI.Xaml.Controls.CommandBar',
    'MenuBar': 'global::Microsoft.UI.Xaml.Controls.MenuBar',
    'StackPanel': 'global::Microsoft.UI.Xaml.Controls.StackPanel',
    'Grid': 'global::Microsoft.UI.Xaml.Controls.Grid',
    'RelativePanel': 'global::Microsoft.UI.Xaml.Controls.RelativePanel',
    'Canvas': 'global::Microsoft.UI.Xaml.Controls.Canvas',
    'Border': 'global::Microsoft.UI.Xaml.Controls.Border',
    'ScrollViewer': 'global::Microsoft.UI.Xaml.Controls.ScrollViewer',
    'ScrollBar': 'global::Microsoft.UI.Xaml.Controls.ScrollBar',
    'ProgressBar': 'global::Microsoft.UI.Xaml.Controls.ProgressBar',
    'ProgressRing': 'global::Microsoft.UI.Xaml.Controls.ProgressRing',
    'WebView2': 'global::Microsoft.UI.Xaml.Controls.WebView2',
    'SplitView': 'global::Microsoft.UI.Xaml.Controls.SplitView',
    'TeachingTip': 'global::Microsoft.UI.Xaml.Controls.TeachingTip',
    'InfoBar': 'global::Microsoft.UI.Xaml.Controls.InfoBar',
    'InfoBadge': 'global::Microsoft.UI.Xaml.Controls.InfoBadge',
    'Expander': 'global::Microsoft.UI.Xaml.Controls.Expander',
    'Image': 'global::Microsoft.UI.Xaml.Controls.Image',
    'PersonPicture': 'global::Microsoft.UI.Xaml.Controls.PersonPicture',
    'FontIcon': 'global::Microsoft.UI.Xaml.Controls.FontIcon',
    'SymbolIcon': 'global::Microsoft.UI.Xaml.Controls.SymbolIcon',
    'AutoSuggestBox': 'global::Microsoft.UI.Xaml.Controls.AutoSuggestBox',
    'MenuFlyout': 'global::Microsoft.UI.Xaml.Controls.MenuFlyout',
    'Flyout': 'global::Microsoft.UI.Xaml.Controls.Flyout',
    'VariableSizedWrapGrid': 'global::Microsoft.UI.Xaml.Controls.VariableSizedWrapGrid',
}

XAML_NS = 'http://schemas.microsoft.com/winfx/2006/xaml'

def parse_xaml(xaml_path):
    """Parse XAML and extract x:Class, x:Name elements with types, and base type."""
    try:
        tree = ET.parse(xaml_path)
        root = tree.getroot()
    except Exception as e:
        print(f"  WARNING: Failed to parse {xaml_path}: {e}")
        return None, [], None
    
    xclass = root.get(f'{{{XAML_NS}}}Class')
    if not xclass:
        return None, [], None
    
    root_tag = root.tag.split('}')[-1] if '}' in root.tag else root.tag
    base_type = TYPE_MAP.get(root_tag, f'global::Microsoft.UI.Xaml.Controls.{root_tag}')
    
    names = []
    for elem in root.iter():
        name = elem.get(f'{{{XAML_NS}}}Name')
        if name:
            tag = elem.tag.split('}')[-1] if '}' in elem.tag else elem.tag
            if ':' in tag:
                prefix, local = tag.split(':')
                win_type = f'global::{prefix}:{local}'
            else:
                win_type = TYPE_MAP.get(tag, f'global::Microsoft.UI.Xaml.Controls.{tag}')
            names.append((name, win_type))
    
    return xclass, names, base_type

def gen_gcs(xclass, names, base_type, xaml_rel_path):
    """Generate .g.cs stub content."""
    parts = xclass.split('.')
    class_name = parts[-1]
    namespace = '.'.join(parts[:-1])
    safe_path = xaml_rel_path.replace('\\', '/')
    
    lines = []
    lines.append(f'#pragma checksum "{safe_path}" "{{8829d00f-11b8-4213-878b-770e8597ac16}}" "{{00000000-0000-0000-0000-000000000000}}"')
    lines.append('')
    lines.append(f'namespace {namespace}')
    lines.append('{')
    lines.append(f'    partial class {class_name} : {base_type}')
    lines.append('    {')
    
    # InitializeComponent
    lines.append('        [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.UI.Xaml.Markup.Compiler"," 3.0.0.0")]')
    lines.append('        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]')
    lines.append('        public void InitializeComponent()')
    lines.append('        {')
    lines.append('            if (_contentLoaded) return;')
    lines.append('            _contentLoaded = true;')
    lines.append(f'            global::System.Uri resourceLocator = new global::System.Uri("ms-appx:///{safe_path}");')
    lines.append('            global::Microsoft.UI.Xaml.Application.LoadComponent(this, resourceLocator, global::Microsoft.UI.Xaml.Controls.Primitives.ComponentResourceLocation.Nested);')
    lines.append('        }')
    lines.append('')
    
    # _contentLoaded
    lines.append('        [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.UI.Xaml.Markup.Compiler"," 3.0.0.0")]')
    lines.append('        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]')
    lines.append('        private bool _contentLoaded;')
    
    # x:Name fields
    for name, wtype in names:
        lines.append('')
        lines.append('        [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.UI.Xaml.Markup.Compiler"," 3.0.0.0")]')
        lines.append('        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]')
        lines.append(f'        internal {wtype} {name};')
    
    # IComponentConnector methods
    lines.append('')
    lines.append('        [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.UI.Xaml.Markup.Compiler"," 3.0.0.0")]')
    lines.append('        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]')
    lines.append('        void global::Microsoft.UI.Xaml.Markup.IComponentConnector.Connect(int connectionId, object target)')
    lines.append('        {')
    if names:
        lines.append('            switch(connectionId)')
        lines.append('            {')
        for i, (name, wtype) in enumerate(names, start=2):
            lines.append(f'            case {i}:')
            lines.append(f'                this.{name} = ({wtype})target;')
            lines.append(f'                break;')
        lines.append('            }')
    lines.append('            this._contentLoaded = true;')
    lines.append('        }')
    
    lines.append('    }')
    lines.append('}')
    lines.append('')
    
    return '\n'.join(lines)


def main():
    input_path = os.path.join(OBJ_DIR, "input.json")
    if not os.path.exists(input_path):
        print(f"ERROR: input.json not found at {input_path}")
        print("Running dotnet build to generate input.json...")
        sys.exit(1)
    
    with open(input_path, 'r', encoding='utf-8') as f:
        input_data = json.load(f)
    
    # Collect all XAML sources from XamlApplications and XamlPages
    items = []
    for app in input_data.get('XamlApplications', []):
        if isinstance(app, dict):
            items.append((app.get('FullPath', ''), app.get('ItemSpec', '')))
        elif isinstance(app, str):
            items.append((os.path.join(PROJECT_DIR, app), app))
    for page in input_data.get('XamlPages', []):
        if isinstance(page, dict):
            items.append((page.get('FullPath', ''), page.get('ItemSpec', '')))
        elif isinstance(page, str):
            items.append((os.path.join(PROJECT_DIR, page), page))
    
    # Also check SdkXamlPages
    sdk_pages = input_data.get('SdkXamlPages') or []
    for page in sdk_pages:
        if isinstance(page, dict):
            items.append((page.get('FullPath', ''), page.get('ItemSpec', '')))
        elif isinstance(page, str):
            items.append((os.path.join(PROJECT_DIR, page), page))
    
    if not items:
        print("ERROR: No XAML items found")
        sys.exit(1)
    
    print(f"Found {len(items)} XAML items")
    
    generated = 0
    for xaml_path, source in items:
        if not source:
            source = os.path.relpath(xaml_path, PROJECT_DIR) if xaml_path else ''
        if not source or not source.endswith('.xaml'):
            continue
        
        if not os.path.exists(xaml_path):
            print(f"  SKIP {source}: file not found")
            continue
        
        xclass, names, base_type = parse_xaml(xaml_path)
        if not xclass:
            print(f"  SKIP {source}: no x:Class")
            continue
        
        rel_dir = os.path.dirname(source)
        out_dir = os.path.join(OBJ_DIR, rel_dir) if rel_dir else OBJ_DIR
        os.makedirs(out_dir, exist_ok=True)
        
        basename = os.path.splitext(os.path.basename(source))[0]
        out_path = os.path.join(out_dir, f"{basename}.g.cs")
        
        gcs_content = gen_gcs(xclass, names, base_type, source)
        
        with open(out_path, 'w', encoding='utf-8') as f:
            f.write(gcs_content)
        
        generated += 1
        print(f"  OK {source} ({len(names)} names)")
    
    print(f"\nDone! Generated {generated} .g.cs files in {OBJ_DIR}")

if __name__ == '__main__':
    main()
