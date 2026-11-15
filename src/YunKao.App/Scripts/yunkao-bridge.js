// 融智云考页面桥接器：只由白名单 host 的 C# WebViewService 注入。
(function () {
  "use strict";

  if (window.__yunkaoBridgeInstalled) return;
  window.__yunkaoBridgeInstalled = true;

  const post = (message) => chrome.webview.postMessage(JSON.stringify(message));
  let readyTimer = null;
  let lastMarker = "";

  const QUESTION_ROOT_SELECTOR =
    ".practice_slide_content, .question-content, .exam-item, .exam_question, .subject_item";

  function findActiveQuestionRoot() {
    return document.querySelector(".swiper-slide-active") || document.querySelector(QUESTION_ROOT_SELECTOR);
  }

  function getPageState() {
    const isLogin = !!document.querySelector("input[type='password'], input[name='password'], #password");
    const active = findActiveQuestionRoot();
    return {
      isLogin: isLogin,
      isPractice: !!active,
      url: window.location.href,
      title: document.title
    };
  }

  function readMarker() {
    const current = document.querySelector(".swiper-pagination-current");
    const total = document.querySelector("#swiper-total");
    const active = findActiveQuestionRoot();
    if (!active) return null;

    const content = active.querySelector(".practice_slide_content[data-questionid], .practice_slide_content[data-question-id], .practice_slide_content[data-id]");
    const questionId = active.dataset.questionid || active.dataset.questionId || active.dataset.id
      || content?.dataset?.questionid || content?.dataset?.questionId || content?.dataset?.id || "";
    const currentText = current ? current.textContent.trim() : "";
    const totalText = total ? total.textContent.trim() : "";
    const title = active.querySelector(".practice_slide_title, .title, .txt");
    const titleText = title ? title.textContent.replace(/\s+/g, " ").trim() : "";
    const marker = questionId || `${currentText}/${totalText}|${titleText}`;
    return { marker, questionId, current: currentText, total: totalText };
  }

  function checkPracticeState() {
    const state = getPageState();
    post({ type: "pageState", ...state });
    return state;
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
    if (!data || typeof data !== "object" || !getPageState().isLogin) return;
    if (data.schoolCode) setValue("input[name='school'], input[name='schoolCode'], input[name='schoolcode'], #schoolCode, #schoolcode, #school_code, input[placeholder*='学校']", data.schoolCode);
    if (data.user) setValue("input[name='username'], input[name='user'], input[name='account'], #username, input[placeholder*='账号']", data.user);
    if (data.password) setValue("input[type='password'], input[name='password'], #password, #password_hide", data.password);
    post({ type: "credentialsFilled" });
  }

  function isPracticeReady() {
    const state = getPageState();
    return state.isPractice && !state.isLogin;
  }

  function getActiveQuestionHtml() {
    const active = findActiveQuestionRoot();
    return active ? active.outerHTML : "";
  }

  function readMarkerValue() {
    const state = readMarker();
    return state ? state.marker : "";
  }

  window.YunKaoBridge = {
    next,
    emitReady,
    getPageState,
    checkPracticeState,
    isPracticeReady,
    getActiveQuestionHtml,
    readMarker,
    readMarkerValue
  };
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
