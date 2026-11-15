融智云考桌面版 2.0.0

开始使用

1. 解压整个文件夹，运行 YunKao.App.exe。请保留同目录的运行库、worker 和 Scripts。
2. 在工作台的云考网页中登录，打开具体的练习题。学校代码目前固定为 u101441。
3. 点击右上角“提取任务”，然后点击“开始提取”。暂停后可以继续；停止后已保存的题目仍可导出。
4. 在任务面板选择 PDF、DOCX，或从“更多”选择 Markdown、TXT。主操作“导出题库”使用设置中的默认格式。练习版隐藏答案和解析。
5. 历史页可恢复已保存的题库，导出记录页可打开文件或重新导出。切换页面会保留工作台任务。

运行条件

内置网页需要 Microsoft Edge WebView2 Evergreen Runtime。
PDF 导出需要本机安装可用的 Word 或 WPS。没有这些组件时，可先导出 DOCX、Markdown 或 TXT。
本包附带 .NET 运行库和 Python Worker，无需另装 Python。

常用操作

Esc：关闭任务面板。
Ctrl+L：编辑地址。
Ctrl+R / F5：刷新网页。
Alt+左 / Alt+右：后退 / 前进。

账号与数据

密码和 API Key 保存到 Windows Credential Manager。登录时，凭据会提交给云考网站。
配置：%APPDATA%\YunKaoDesktop\settings.json
题库历史：%LOCALAPPDATA%\YunKaoDesktop\yunkao.db
导出文件默认保存在系统文档目录，可在设置中更改。

AI 补全默认关闭。启用后，缺少答案的题目会发送到你选择的 API 服务；图片识别也会处理题目图片。生成答案需要核对。
遇到加载、登录或导出问题时，可查看诊断页。题库资料免费获取，请勿倒卖。
