"""Static UI contracts, not a screenshot or live WPF interaction test."""
from pathlib import Path
import xml.etree.ElementTree as E
root=Path(__file__).resolve().parent.parent/'RobotVirtualSubnetDebugger'
for p in root.rglob('*.xaml'): E.parse(p)
ns={'w':'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
easy=E.parse(root/'Views/EasyModeView.xaml')
fields=easy.findall('.//w:TextBox',ns)
key=next(x for x in fields if 'SharedKey' in x.get('Text',''))
assert key.get('IsReadOnly')=='{Binding IsGatewayRole}'
tutorial=(root/'Views/TutorialView.xaml').read_text()
assert '不需要复制粘贴密钥' not in tutorial and '程序会自动完成配对' not in tutorial
assert '（以实机为准）0' not in tutorial and '不要填机械臂的 192.168.57.2' in tutorial
assert '单端口代理不能替代' in tutorial and '不要直接运行运动示例' in tutorial
main=(root/'MainWindow.xaml').read_text()
assert '不修改系统网络配置' not in main and '应用会修改网络' in main
assert 'IsKeyboardFocused' in (root/'App.xaml').read_text()
assert 'Width="390"' not in (root/'Views/EasyModeView.xaml').read_text()
print('PASS: XML well-formed; editable B key; accurate tutorial/safety; keyboard focus; flexible wizard columns. No live GUI tested.')
