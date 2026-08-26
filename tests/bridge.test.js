const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function loadBridge(activeSelector) {
  const messages = [];
  const active = {
    dataset: { questionid: 'q-42' },
    outerHTML: `<div class="${activeSelector.slice(1)}">题目</div>`,
    querySelector: () => ({ textContent: '题目' }),
  };
  const document = {
    readyState: 'complete',
    title: '练习',
    body: {},
    querySelector(selector) {
      if (selector.includes('password')) return null;
      if (selector === '.swiper-pagination-current') return { textContent: '42' };
      if (selector === '#swiper-total') return { textContent: '100' };
      if (selector.includes(activeSelector)) return active;
      return null;
    },
  };
  const context = {
    window: {
      location: { href: 'https://www.cctrcloud.net/exam/1' },
      document,
    },
    document,
    chrome: {
      webview: {
        postMessage: message => messages.push(message),
        addEventListener() {},
      },
    },
    MutationObserver: class { observe() {} },
    setTimeout(callback) { callback(); return 1; },
    clearTimeout() {},
  };
  context.window.chrome = context.chrome;
  vm.runInNewContext(
    fs.readFileSync(path.resolve(__dirname, '../src/YunKao.App/Scripts/yunkao-bridge.js'), 'utf8'),
    context);
  return { bridge: context.window.YunKaoBridge, messages, active };
}

test('all question operations use the same active root for question-content pages', () => {
  const { bridge, messages, active } = loadBridge('.question-content');

  assert.equal(bridge.isPracticeReady(), true);
  assert.equal(bridge.getActiveQuestionHtml(), active.outerHTML);
  assert.equal(bridge.readMarkerValue(), 'q-42');
  assert.equal(messages.find(message => message.type === 'pageState').isPractice, true);
});

test('exam-item pages also produce the same ready and marker signals', () => {
  const { bridge } = loadBridge('.exam-item');

  assert.equal(bridge.isPracticeReady(), true);
  assert.equal(bridge.readMarkerValue(), 'q-42');
});
