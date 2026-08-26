/*
 * 认证页的一点点交互。刻意不引任何框架 —— 这三件事加起来不到 80 行,
 * 而登录页是整个市场里最不该因为一个 CDN 挂了就打不开的一页。
 */
(function () {
  'use strict';

  /* ---- 口令明文切换 --------------------------------------------------------
     口令输错三次的人里,多数只是没发现大写锁定开着。 */
  document.querySelectorAll('[data-reveal]').forEach(function (button) {
    button.addEventListener('click', function () {
      var input = document.getElementById(button.getAttribute('data-reveal'));
      if (!input) return;
      var shown = input.type === 'text';
      input.type = shown ? 'password' : 'text';
      button.setAttribute('aria-label', shown ? '显示口令' : '隐藏口令');
      button.classList.toggle('is-on', !shown);
    });
  });

  /* ---- 口令强度 ------------------------------------------------------------
     只做长度与字符类别这几条**能自己判断**的:不臆测词典、不给"很安全"这种承诺。
     真正的口令策略在服务端(AccountStore),这里只是让人在提交之前就看见结论。 */
  var RULES = [
    { key: 'len', label: '至少 8 位', test: function (value) { return value.length >= 8; } },
    { key: 'digit', label: '含数字', test: function (value) { return /\d/.test(value); } },
    { key: 'case', label: '含大小写', test: function (value) { return /[a-z]/.test(value) && /[A-Z]/.test(value); } },
    { key: 'self', label: '不与用户名相同', test: function (value, userName) { return !userName || value.toLowerCase() !== userName.toLowerCase(); } }
  ];
  var LABELS = ['太短', '偏弱', '一般', '不错', '很强'];

  document.querySelectorAll('[data-strength-for]').forEach(function (meter) {
    var input = document.getElementById(meter.getAttribute('data-strength-for'));
    var userNameInput = document.getElementById(meter.getAttribute('data-strength-username') || '');
    if (!input) return;

    var bars = document.createElement('div');
    bars.className = 'strength-bars';
    bars.innerHTML = '<i></i><i></i><i></i><i></i>';

    var meta = document.createElement('div');
    meta.className = 'strength-meta';
    var label = document.createElement('span');
    label.className = 'strength-label';
    var tail = document.createElement('span');
    meta.appendChild(label);
    meta.appendChild(tail);

    var rules = document.createElement('div');
    rules.className = 'strength-rules';
    var ruleNodes = RULES.map(function (rule) {
      var node = document.createElement('span');
      node.textContent = rule.label;
      rules.appendChild(node);
      return node;
    });

    meter.appendChild(bars);
    meter.appendChild(meta);
    meter.appendChild(rules);

    function refresh() {
      var value = input.value || '';
      var userName = userNameInput ? userNameInput.value : '';
      var met = RULES.map(function (rule, index) {
        var ok = value.length > 0 && rule.test(value, userName);
        ruleNodes[index].className = ok ? 'met' : '';
        return ok;
      });
      var score = met.filter(Boolean).length;
      // 12 位以上直接顶格:长度是这几条里唯一真正抬高破解成本的。
      if (value.length >= 12 && score >= 3) score = 4;
      meter.setAttribute('data-score', value.length === 0 ? '0' : String(score));
      label.textContent = value.length === 0 ? '还没输入' : '强度:' + LABELS[score];
      tail.textContent = value.length > 0 && score < 4 ? '再长一点会更稳' : '';
    }

    input.addEventListener('input', refresh);
    if (userNameInput) userNameInput.addEventListener('input', refresh);
    refresh();
  });

  /* ---- 复制 sub ------------------------------------------------------------ */
  document.querySelectorAll('[data-copy]').forEach(function (button) {
    button.addEventListener('click', function () {
      var text = button.getAttribute('data-copy');
      var done = function () {
        var original = button.querySelector('span');
        if (!original) return;
        var previous = original.textContent;
        original.textContent = '已复制';
        setTimeout(function () {
          original.textContent = previous;
        }, 1600);
      };
      if (navigator.clipboard) {
        navigator.clipboard.writeText(text).then(done, function () {});
      }
    });
  });
})();
