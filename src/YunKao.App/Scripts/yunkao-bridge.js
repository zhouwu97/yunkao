// 融智云考页面桥接器：只由白名单 host 的 C# WebViewService 注入。
(function () {
  "use strict";

  if (window.__yunkaoBridgeInstalled) return;
  window.__yunkaoBridgeInstalled = true;

  const post = (message) => chrome.webview.postMessage(message);
  let readyTimer = null;
  let lastMarker = "";

  const QUESTION_ROOT_SELECTOR =
    ".swiper-slide-active, .practice_slide_content, .question-content, .exam-item, .exam_question, .subject_item";

  function findActiveQuestionRoot() {
    return document.querySelector(QUESTION_ROOT_SELECTOR);
  }

  function readMarker() {
    const current = document.querySelector(".swiper-pagination-current");
    const total = document.querySelector("#swiper-total");
    const active = findActiveQuestionRoot();
    if (!active) return null;

    const questionId = active.dataset.questionid || active.dataset.questionId || active.dataset.id || "";
    const currentText = current ? current.textContent.trim() : "";
    const totalText = total ? total.textContent.trim() : "";
    const title = active.querySelector(".practice_slide_title, .title, .txt");
    const titleText = title ? title.textContent.replace(/\s+/g, " ").trim() : "";
    const marker = questionId || `${currentText}/${totalText}|${titleText}`;
    return { marker, questionId, current: currentText, total: totalText };
  }

  function checkPracticeState() {
    const isLogin = !!document.querySelector("input[type='password'], input[name='password'], #password");
    const active = findActiveQuestionRoot();
    const isPractice = !!active;
    post({
      type: "pageState",
      isLogin: isLogin,
      isPractice: isPractice,
      url: window.location.href,
      title: document.title
    });
  }

  function emitReady() {
    checkPracticeState();
    const state = readMarker();
    if (!state || !state.marker || state.marker === lastMarker) return;
    lastMarker = state.marker;
    post({ type: "questionReady", marker: state.marker, questionId: state.questionId, current: state.current, total: state.total });
  }

  function scheduleReady() {
    if (readyTimer) clearTimeout(readyTimer);
    readyTimer = setTimeout(emitReady, 100);
  }

  function next() {
    const button = document.querySelector(
      ".swiper-button-next:not(.swiper-button-disabled), .next-question, [data-action='next'], button[aria-label*='下一']"
    );
    if (button) {
      button.click();
      return true;
    }
    return false;
  }

  function setValue(selector, value) {
    const element = document.querySelector(selector);
    if (!element || value === undefined || value === null) return false;
    element.value = value;
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
    return true;
  }

  function fillCredentials(data) {
    if (!data || typeof data !== "object") return;
    setValue("input[name='school'], input[name='schoolCode'], #schoolCode", data.schoolCode);
    setValue("input[name='username'], input[name='user'], input[name='account'], #username", data.user);
    setValue("input[type='password'], input[name='password'], #password", data.password);
    post({ type: "credentialsFilled" });
  }

  window.YunKaoBridge = { next, emitReady };
  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener("message", (event) => {
      if (!event.data) return;
      if (event.data.type === "fillCredentials") fillCredentials(event.data);
    });
  }

  const observer = new MutationObserver(scheduleReady);
  const start = () => {
    if (!document.body) return;
    observer.observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true });
    post({ type: "bridgeReady" });
    scheduleReady();
  };
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
  else start();
})();
