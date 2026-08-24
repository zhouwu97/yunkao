// 第一阶段不向任何页面注入脚本。
// 后续实现时，只有通过 cctrcloud.net host 白名单的 WebView2 页面才会安装桥接器。
(function () {
  "use strict";
  // 保留独立脚本文件，避免把未来的 MutationObserver 字符串散落在 C# 代码中。
})();
