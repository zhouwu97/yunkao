"""
测试爬虫脚本：分析融智云考练习页面 DOM 结构
目的：搞清楚答案在 HTML 中的真实结构，以及为什么提取出来的答案是错误的
"""

import sys
import io
# Fix Windows GBK encoding issue
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='replace')

import time
import json
import os
from selenium import webdriver
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.chrome.service import Service

# ============ 配置 ============
SCHOOL_CODE = "u101441"
STUDENT_ID = "2403060128"
PASSWORD = "111111"

# 目标练习页面
TARGET_URL = (
    "https://www.cctrcloud.net/practice/subject_practice.html"
    "?studentpractise_id=2711631&a=0&practiseid=25131&courseid=111393"
    "&teacherid=27004&coursename=2026%25E6%2598%25A5%25E6%25AF%259B%25E6%25A6%25822"
    "&studentpractisequestioncount=184&isaiquestion=0"
)

LOGIN_URL = "https://www.cctrcloud.net/practice/login.html"

# 输出目录
OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "crawl_output")
os.makedirs(OUTPUT_DIR, exist_ok=True)


def create_driver():
    """创建 Chrome WebDriver"""
    options = Options()
    options.add_argument("--headless=new")  # 开启无头模式，避免弹窗被误关
    options.add_argument("--no-sandbox")
    options.add_argument("--disable-dev-shm-usage")
    options.add_argument("--disable-gpu")
    options.add_argument("--window-size=1920,1080")
    options.add_argument("--disable-blink-features=AutomationControlled")
    options.add_experimental_option("excludeSwitches", ["enable-automation"])
    
    driver = webdriver.Chrome(options=options)
    driver.implicitly_wait(10)
    return driver


def login(driver):
    """登录融智云考系统"""
    print(f"[1] 正在访问登录页: {LOGIN_URL}")
    driver.get(LOGIN_URL)
    time.sleep(3)
    
    # 保存登录页面 HTML 以供分析
    save_html(driver, "01_login_page.html")
    
    # 查找并填写登录表单
    print("[2] 正在填写登录信息...")
    
    # 尝试找到所有 input 元素并分析
    inputs = driver.find_elements(By.TAG_NAME, "input")
    print(f"    找到 {len(inputs)} 个 input 元素:")
    for idx, inp in enumerate(inputs):
        inp_type = inp.get_attribute("type")
        placeholder = inp.get_attribute("placeholder") or ""
        name = inp.get_attribute("name") or ""
        inp_id = inp.get_attribute("id") or ""
        print(f"    [{idx}] type={inp_type}, placeholder='{placeholder}', name='{name}', id='{inp_id}'")
    
    # 填写学校编码
    filled = 0
    for inp in inputs:
        placeholder = (inp.get_attribute("placeholder") or "").lower()
        name = (inp.get_attribute("name") or "").lower()
        inp_type = inp.get_attribute("type") or ""
        
        if any(kw in placeholder for kw in ["学校", "机构", "school"]) or "schoolcode" in name:
            inp.clear()
            inp.send_keys(SCHOOL_CODE)
            print(f"    ✅ 填入学校编码: {SCHOOL_CODE}")
            filled += 1
        elif any(kw in placeholder for kw in ["学号", "账号", "用户"]) or "studentno" in name or "username" in name:
            inp.clear()
            inp.send_keys(STUDENT_ID)
            print(f"    ✅ 填入学号: {STUDENT_ID}")
            filled += 1
        elif inp_type == "password":
            inp.clear()
            inp.send_keys(PASSWORD)
            print(f"    ✅ 填入密码: ******")
            filled += 1
    
    if filled < 3:
        print(f"    ⚠️ 只填写了 {filled} 个字段，尝试用 JS 注入方式...")
        # 使用 JS 注入方式
        driver.execute_script(f"""
            var inputs = document.querySelectorAll('input');
            inputs.forEach(function(inp) {{
                var p = inp.placeholder || '';
                if (p.includes('学校') || p.includes('机构')) {{
                    inp.value = '{SCHOOL_CODE}';
                    inp.dispatchEvent(new Event('input', {{ bubbles: true }}));
                }} else if (p.includes('学号') || p.includes('账号') || p.includes('用户名')) {{
                    inp.value = '{STUDENT_ID}';
                    inp.dispatchEvent(new Event('input', {{ bubbles: true }}));
                }} else if (inp.type === 'password') {{
                    inp.value = '{PASSWORD}';
                    inp.dispatchEvent(new Event('input', {{ bubbles: true }}));
                }}
            }});
        """)
        time.sleep(1)
    
    save_html(driver, "02_login_filled.html")
    
    # 点击登录按钮
    print("[3] 正在点击登录按钮...")
    try:
        login_btn = driver.find_element(By.ID, "loginbtn")
    except Exception as e:
        print(f"    ❌ 找不到登录按钮！{e}")
        return False
    
    login_btn.click()
    print("    ✅ 已点击登录按钮")
    time.sleep(5)
    
    save_html(driver, "03_after_login.html")
    
    # 检查是否登录成功
    current_url = driver.current_url
    print(f"    当前 URL: {current_url}")
    if "login" not in current_url.lower() or "index" in current_url.lower():
        print("    ✅ 登录成功!")
        return True
    else:
        print("    ⚠️ 可能登录失败，继续尝试...")
        return True  # 继续执行以收集更多信息


def navigate_to_practice(driver):
    """导航到练习页面"""
    print(f"\n[4] 正在导航到练习页面...")
    print(f"    URL: {TARGET_URL}")
    driver.get(TARGET_URL)
    time.sleep(5)
    
    save_html(driver, "04_practice_page.html")
    print(f"    当前 URL: {driver.current_url}")


def analyze_question_structure(driver, question_num=1):
    """深度分析题目 DOM 结构"""
    print(f"\n[5] 正在分析第 {question_num} 题的 DOM 结构...")
    
    # 1. 获取完整的 body innerHTML
    full_html = driver.execute_script("return document.body.innerHTML;")
    save_text(full_html, f"05_full_body_{question_num}.html")
    
    # 2. 获取当前活动 slide 的详细结构
    slide_info = driver.execute_script("""
        var result = {};
        
        // 查找当前活动的 slide
        var activeSlide = document.querySelector('.swiper-slide-active');
        result.hasActiveSlide = !!activeSlide;
        
        if (activeSlide) {
            result.activeSlideHTML = activeSlide.innerHTML;
            result.activeSlideOuterHTML = activeSlide.outerHTML.substring(0, 500);
            result.activeSlideClasses = activeSlide.className;
        }
        
        // 查找 practice_slide_content
        var slideContent = document.querySelector('.swiper-slide-active .practice_slide_content.slide-con') || 
                          document.querySelector('.practice_slide_content.slide-con');
        result.hasSlideContent = !!slideContent;
        
        if (slideContent) {
            result.slideContentHTML = slideContent.innerHTML;
            result.slideContentDataId = slideContent.getAttribute('data-id');
            result.slideContentDataChapterId = slideContent.getAttribute('data-chapterid');
            result.slideContentAllAttributes = {};
            for (var i = 0; i < slideContent.attributes.length; i++) {
                var attr = slideContent.attributes[i];
                result.slideContentAllAttributes[attr.name] = attr.value;
            }
        }
        
        // 所有 slide 数量
        result.totalSlides = document.querySelectorAll('.swiper-slide').length;
        result.totalSlideContents = document.querySelectorAll('.practice_slide_content').length;
        
        return result;
    """)
    save_json(slide_info, f"06_slide_info_{question_num}.json")
    print(f"    活动 Slide: {slide_info.get('hasActiveSlide')}")
    print(f"    Slide Content: {slide_info.get('hasSlideContent')}")
    print(f"    总 Slides: {slide_info.get('totalSlides')}")
    
    # 3. 深度分析题目标题和类型
    question_info = driver.execute_script("""
        var result = {};
        var slideContent = document.querySelector('.swiper-slide-active .practice_slide_content.slide-con') || 
                          document.querySelector('.practice_slide_content.slide-con');
        if (!slideContent) return {error: 'No slideContent found'};
        
        // 题目标题
        var titleEl = slideContent.querySelector('.practice_slide_title .title');
        result.titleText = titleEl ? titleEl.textContent.trim() : null;
        result.titleHTML = titleEl ? titleEl.innerHTML : null;
        
        // 题目类型
        var typeEl = slideContent.querySelector('.practice_slide_title .type');
        result.typeText = typeEl ? typeEl.textContent.trim() : null;
        result.typeHTML = typeEl ? typeEl.innerHTML : null;
        
        // 完整标题区域
        var titleArea = slideContent.querySelector('.practice_slide_title');
        result.titleAreaHTML = titleArea ? titleArea.innerHTML : null;
        
        return result;
    """)
    save_json(question_info, f"07_question_info_{question_num}.json")
    print(f"    题目类型: {question_info.get('typeText')}")
    print(f"    题目标题: {(question_info.get('titleText') or '')[:60]}...")
    
    # 4. 深度分析选项结构 (关键！)
    options_info = driver.execute_script("""
        var result = {options: [], rawHTML: ''};
        var slideContent = document.querySelector('.swiper-slide-active .practice_slide_content.slide-con') || 
                          document.querySelector('.practice_slide_content.slide-con');
        if (!slideContent) return {error: 'No slideContent found'};
        
        // 获取选项容器的原始 HTML
        var optionContainer = slideContent.querySelector('.option_content');
        result.rawHTML = optionContainer ? optionContainer.innerHTML : 'NOT_FOUND';
        result.optionContainerClass = optionContainer ? optionContainer.className : '';
        
        // 逐个分析选项
        var optionEls = slideContent.querySelectorAll('.option_content li');
        result.optionCount = optionEls.length;
        
        for (var i = 0; i < optionEls.length; i++) {
            var li = optionEls[i];
            var opt = {
                index: i,
                outerHTML: li.outerHTML,
                className: li.className,
                allAttributes: {},
                // 分析所有子元素
                childrenInfo: []
            };
            
            // 收集所有属性
            for (var j = 0; j < li.attributes.length; j++) {
                var attr = li.attributes[j];
                opt.allAttributes[attr.name] = attr.value;
            }
            
            // letterArr 元素
            var letterEl = li.querySelector('.letterArr');
            opt.letterArrText = letterEl ? letterEl.textContent.trim() : null;
            opt.letterArrHTML = letterEl ? letterEl.innerHTML : null;
            
            // txt 元素
            var txtEl = li.querySelector('.txt');
            opt.txtText = txtEl ? txtEl.textContent.trim() : null;
            opt.txtHTML = txtEl ? txtEl.innerHTML : null;
            
            // input 元素 (关键：data-isright)
            var inputEl = li.querySelector('input');
            if (inputEl) {
                opt.inputType = inputEl.type;
                opt.inputAllAttributes = {};
                for (var k = 0; k < inputEl.attributes.length; k++) {
                    var iattr = inputEl.attributes[k];
                    opt.inputAllAttributes[iattr.name] = iattr.value;
                }
                opt.dataIsRight = inputEl.getAttribute('data-isright');
                opt.inputChecked = inputEl.checked;
                opt.inputValue = inputEl.value;
                opt.inputName = inputEl.name;
            } else {
                opt.inputExists = false;
            }
            
            // 检查 right_ans_mark
            var rightMark = li.querySelector('.right_ans_mark');
            opt.hasRightAnsMark = !!rightMark;
            opt.rightAnsMarkHTML = rightMark ? rightMark.innerHTML : null;
            
            // 检查 is-right / correct 类名
            opt.hasIsRightClass = li.classList.contains('is-right');
            opt.hasCorrectClass = li.classList.contains('correct');
            
            // 检查 i.right
            var iRight = li.querySelector('i.right');
            opt.hasIRight = !!iRight;
            
            // 分析子节点
            for (var c = 0; c < li.children.length; c++) {
                var child = li.children[c];
                opt.childrenInfo.push({
                    tagName: child.tagName,
                    className: child.className,
                    text: child.textContent.trim().substring(0, 50),
                    attributeCount: child.attributes.length
                });
            }
            
            result.options.push(opt);
        }
        
        return result;
    """)
    save_json(options_info, f"08_options_detail_{question_num}.json")
    print(f"    选项数量: {options_info.get('optionCount')}")
    
    if options_info.get('options'):
        for opt in options_info['options']:
            letter = opt.get('letterArrText', f"[{opt['index']}]")
            txt = (opt.get('txtText') or '')[:40]
            data_is_right = opt.get('dataIsRight', 'N/A')
            has_mark = opt.get('hasRightAnsMark', False)
            checked = opt.get('inputChecked', 'N/A')
            print(f"      {letter}: {txt}... | data-isright={data_is_right} | right_mark={has_mark} | checked={checked}")
    
    # 5. 分析答案区域
    answer_info = driver.execute_script("""
        var result = {};
        var slideContent = document.querySelector('.swiper-slide-active .practice_slide_content.slide-con') || 
                          document.querySelector('.practice_slide_content.slide-con');
        if (!slideContent) return {error: 'No slideContent found'};
        
        // 答案文本
        var answerText = slideContent.querySelector('.answer-text');
        result.answerText = answerText ? answerText.textContent.trim() : null;
        result.answerTextHTML = answerText ? answerText.innerHTML : null;
        
        // right_ans_mark
        var rightMark = slideContent.querySelector('.right_ans_mark');
        result.rightAnsMarkText = rightMark ? rightMark.textContent.trim() : null;
        result.rightAnsMarkHTML = rightMark ? rightMark.innerHTML : null;
        
        // 解析区域
        var analysis = slideContent.querySelector('.analysis-content .desc');
        result.analysisText = analysis ? analysis.textContent.trim() : null;
        result.analysisHTML = analysis ? analysis.innerHTML : null;
        
        // practice_analysis
        var practiceAnalysis = slideContent.querySelector('.practice_analysis');
        result.practiceAnalysisText = practiceAnalysis ? practiceAnalysis.textContent.trim() : null;
        result.practiceAnalysisHTML = practiceAnalysis ? practiceAnalysis.innerHTML : null;
        
        // 搜索所有可能包含 "答案" 文本的元素
        var allElements = slideContent.querySelectorAll('*');
        result.elementsWithAnswer = [];
        for (var i = 0; i < allElements.length; i++) {
            var el = allElements[i];
            if (el.children.length === 0 && el.textContent.includes('答案')) {
                result.elementsWithAnswer.push({
                    tag: el.tagName,
                    className: el.className,
                    text: el.textContent.trim().substring(0, 100),
                    parentClass: el.parentElement ? el.parentElement.className : ''
                });
            }
        }
        
        // 搜索所有包含 "正确" 或 "right" 的元素
        result.elementsWithRight = [];
        for (var i = 0; i < allElements.length; i++) {
            var el = allElements[i];
            var cls = el.className || '';
            if (typeof cls === 'string' && (cls.includes('right') || cls.includes('correct') || cls.includes('answer'))) {
                result.elementsWithRight.push({
                    tag: el.tagName,
                    className: cls,
                    text: el.textContent.trim().substring(0, 100),
                    innerHTML: el.innerHTML.substring(0, 200)
                });
            }
        }
        
        return result;
    """)
    save_json(answer_info, f"09_answer_info_{question_num}.json")
    print(f"    answer-text: {answer_info.get('answerText')}")
    print(f"    right_ans_mark: {answer_info.get('rightAnsMarkText')}")
    print(f"    practice_analysis: {(answer_info.get('practiceAnalysisText') or '')[:60]}")
    print(f"    含 'answer/right/correct' 类名的元素数: {len(answer_info.get('elementsWithRight', []))}")
    
    for el in answer_info.get('elementsWithRight', []):
        print(f"      <{el['tag']} class='{el['className']}'>: {el['text'][:60]}")
    
    # 6. 检查是否需要先 "查看答案" 才能看到答案
    check_answer_button = driver.execute_script("""
        var result = {};
        var slideContent = document.querySelector('.swiper-slide-active .practice_slide_content.slide-con') || 
                          document.querySelector('.practice_slide_content.slide-con');
        if (!slideContent) return {error: 'No slideContent'};
        
        // 查找 "查看答案" / "显示答案" / "查看解析" 按钮
        var allBtns = slideContent.querySelectorAll('button, a, div, span');
        result.answerButtons = [];
        for (var i = 0; i < allBtns.length; i++) {
            var el = allBtns[i];
            var text = el.textContent.trim();
            if (text.includes('查看答案') || text.includes('显示答案') || text.includes('查看解析') || 
                text.includes('看答案') || text.includes('答案解析') || text.includes('提交')) {
                result.answerButtons.push({
                    tag: el.tagName,
                    className: el.className,
                    id: el.id,
                    text: text.substring(0, 50),
                    onClick: el.getAttribute('onclick'),
                    display: window.getComputedStyle(el).display,
                    visibility: window.getComputedStyle(el).visibility
                });
            }
        }
        
        // 检查隐藏的答案区域
        result.hiddenAnswerAreas = [];
        var areas = slideContent.querySelectorAll('.answer-area, .answer-section, .answer-wrap, .practice_answer, .practice_analysis, [class*="answer"], [class*="analysis"]');
        for (var i = 0; i < areas.length; i++) {
            var el = areas[i];
            var style = window.getComputedStyle(el);
            result.hiddenAnswerAreas.push({
                tag: el.tagName,
                className: el.className,
                display: style.display,
                visibility: style.visibility,
                height: style.height,
                overflow: style.overflow,
                text: el.textContent.trim().substring(0, 100)
            });
        }
        
        return result;
    """)
    save_json(check_answer_button, f"10_answer_buttons_{question_num}.json")
    
    if check_answer_button.get('answerButtons'):
        print(f"    找到 {len(check_answer_button['answerButtons'])} 个答案相关按钮:")
        for btn in check_answer_button['answerButtons']:
            print(f"      <{btn['tag']} class='{btn['className']}' id='{btn.get('id', '')}'> text: {btn['text']}")
    
    if check_answer_button.get('hiddenAnswerAreas'):
        print(f"    找到 {len(check_answer_button['hiddenAnswerAreas'])} 个答案相关区域:")
        for area in check_answer_button['hiddenAnswerAreas']:
            print(f"      <{area['tag']} class='{area['className']}'> display={area['display']}, text: {area['text'][:50]}")
    
    return slide_info, question_info, options_info, answer_info


def try_reveal_answer(driver, question_num):
    """尝试点击 "查看答案" 按钮，然后重新分析"""
    print(f"\n[6] 尝试触发答案显示...")
    
    # 先尝试选择一个选项触发答案显示
    result = driver.execute_script("""
        var slideContent = document.querySelector('.swiper-slide-active .practice_slide_content.slide-con') || 
                          document.querySelector('.practice_slide_content.slide-con');
        if (!slideContent) return {error: 'no content'};
        
        var result = {clicked: false, method: ''};
        
        // 方法 1: 点击第一个选项（很多系统需要先选择再提交才看到答案）
        var firstOption = slideContent.querySelector('.option_content li');
        if (firstOption) {
            firstOption.click();
            result.clicked = true;
            result.method = 'clicked first option li';
        }
        
        return result;
    """)
    print(f"    点击选项结果: {result}")
    time.sleep(2)
    
    # 检查是否出现提交/确认按钮
    submit_result = driver.execute_script("""
        var slideContent = document.querySelector('.swiper-slide-active .practice_slide_content.slide-con') || 
                          document.querySelector('.practice_slide_content.slide-con');
        if (!slideContent) return {error: 'no content'};
        
        // 查找提交按钮
        var btns = slideContent.querySelectorAll('button, a, div[class*="btn"], span[class*="btn"], .submit, .confirm');
        var result = {buttons: []};
        for (var i = 0; i < btns.length; i++) {
            var el = btns[i];
            result.buttons.push({
                tag: el.tagName,
                className: el.className,
                text: el.textContent.trim().substring(0, 50),
                display: window.getComputedStyle(el).display
            });
        }
        
        return result;
    """)
    save_json(submit_result, f"11_submit_buttons_{question_num}.json")
    
    # 尝试点击提交/查看答案
    clicked = driver.execute_script("""
        var slideContent = document.querySelector('.swiper-slide-active .practice_slide_content.slide-con') || 
                          document.querySelector('.practice_slide_content.slide-con');
        if (!slideContent) return 'no content';
        
        // 尝试各种触发答案的方式
        var result = [];
        
        // 1. 查看答案按钮
        var answerBtns = slideContent.querySelectorAll('*');
        for (var i = 0; i < answerBtns.length; i++) {
            var el = answerBtns[i];
            var text = el.textContent.trim();
            if ((text === '查看答案' || text === '提交' || text === '确认' || text === '查看解析') 
                && (el.tagName === 'BUTTON' || el.tagName === 'A' || el.tagName === 'DIV' || el.tagName === 'SPAN')) {
                if (el.children.length <= 2) {  // 避免点击包含太多子元素的容器
                    el.click();
                    result.push('clicked: ' + text + ' (' + el.tagName + '.' + el.className + ')');
                }
            }
        }
        
        return result;
    """)
    print(f"    尝试点击答案按钮: {clicked}")
    time.sleep(3)
    
    # 保存点击后的 HTML
    save_html(driver, f"12_after_reveal_{question_num}.html")
    
    # 重新分析答案
    print(f"\n[7] 答案显示后重新分析...")
    return analyze_question_structure(driver, question_num=f"{question_num}_revealed")


def analyze_network_api(driver):
    """分析网络请求，看答案是否通过 API 返回"""
    print(f"\n[8] 分析 JavaScript 全局变量和 API 数据...")
    
    global_data = driver.execute_script("""
        var result = {};
        
        // 检查常见的全局变量
        result.hasQuestionData = typeof questionData !== 'undefined';
        result.hasQuestionList = typeof questionList !== 'undefined';
        result.hasPracticeData = typeof practiceData !== 'undefined';
        
        // 检查 window 上的相关属性
        var windowKeys = Object.keys(window).filter(function(k) {
            return k.toLowerCase().includes('question') || 
                   k.toLowerCase().includes('answer') || 
                   k.toLowerCase().includes('practice') ||
                   k.toLowerCase().includes('exam');
        });
        result.relevantWindowKeys = windowKeys;
        
        // 如果有 Vue 实例，检查其 data
        if (window.__vue__) {
            result.hasVue = true;
            try {
                result.vueData = JSON.stringify(window.__vue__.$data).substring(0, 2000);
            } catch(e) {
                result.vueDataError = e.message;
            }
        }
        
        // 检查是否有 Angular
        if (window.angular) {
            result.hasAngular = true;
        }
        
        // 检查 localStorage 和 sessionStorage
        result.localStorageKeys = [];
        for (var i = 0; i < localStorage.length; i++) {
            var key = localStorage.key(i);
            if (key.toLowerCase().includes('question') || key.toLowerCase().includes('answer') || 
                key.toLowerCase().includes('practice')) {
                result.localStorageKeys.push({
                    key: key,
                    valuePreview: localStorage.getItem(key).substring(0, 200)
                });
            }
        }
        
        result.sessionStorageKeys = [];
        for (var i = 0; i < sessionStorage.length; i++) {
            var key = sessionStorage.key(i);
            if (key.toLowerCase().includes('question') || key.toLowerCase().includes('answer') || 
                key.toLowerCase().includes('practice')) {
                result.sessionStorageKeys.push({
                    key: key,
                    valuePreview: sessionStorage.getItem(key).substring(0, 200)
                });
            }
        }
        
        return result;
    """)
    save_json(global_data, "13_global_data.json")
    print(f"    相关全局变量: {global_data.get('relevantWindowKeys', [])}")
    print(f"    localStorage 相关项: {len(global_data.get('localStorageKeys', []))}")
    print(f"    sessionStorage 相关项: {len(global_data.get('sessionStorageKeys', []))}")
    
    # 尝试获取 Vue 组件数据
    vue_data = driver.execute_script("""
        var result = {};
        
        // Vue 2 方式
        var app = document.querySelector('#app');
        if (app && app.__vue__) {
            result.vue2Found = true;
            try {
                var vm = app.__vue__;
                result.vue2DataKeys = Object.keys(vm.$data || {});
                // 尝试获取题目相关数据
                var data = vm.$data;
                for (var key in data) {
                    if (key.toLowerCase().includes('question') || key.toLowerCase().includes('answer') ||
                        key.toLowerCase().includes('option') || key.toLowerCase().includes('right') ||
                        key.toLowerCase().includes('practice') || key.toLowerCase().includes('current')) {
                        result['vue2_' + key] = JSON.stringify(data[key]).substring(0, 500);
                    }
                }
            } catch(e) {
                result.vue2Error = e.message;
            }
        }
        
        // Vue 3 方式
        if (app && app._vnode) {
            result.vue3Found = true;
        }
        
        // 尝试遍历所有元素找 __vue__
        var allEls = document.querySelectorAll('*');
        result.vueComponents = [];
        for (var i = 0; i < Math.min(allEls.length, 1000); i++) {
            if (allEls[i].__vue__) {
                var vm = allEls[i].__vue__;
                var dataKeys = Object.keys(vm.$data || {});
                if (dataKeys.length > 0) {
                    result.vueComponents.push({
                        tag: allEls[i].tagName,
                        className: allEls[i].className.substring(0, 100),
                        dataKeys: dataKeys
                    });
                }
            }
        }
        
        return result;
    """)
    save_json(vue_data, "14_vue_data.json")
    
    if vue_data.get('vue2Found'):
        print(f"    Vue 2 实例数据 keys: {vue_data.get('vue2DataKeys', [])}")
    if vue_data.get('vueComponents'):
        print(f"    Vue 组件数: {len(vue_data['vueComponents'])}")
        for comp in vue_data['vueComponents'][:5]:
            print(f"      <{comp['tag']}> dataKeys: {comp['dataKeys']}")


def analyze_multiple_questions(driver, count=3):
    """分析多道题目，对比答案结构"""
    print(f"\n[9] 分析前 {count} 道题目对比...")
    
    all_questions = []
    
    for i in range(count):
        print(f"\n{'='*60}")
        print(f"  === 第 {i+1} 题 ===")
        print(f"{'='*60}")
        
        slide_info, question_info, options_info, answer_info = analyze_question_structure(driver, question_num=i+1)
        
        q = {
            "num": i + 1,
            "type": question_info.get('typeText'),
            "title": (question_info.get('titleText') or '')[:80],
            "optionCount": options_info.get('optionCount'),
            "options": [],
            "answer_text": answer_info.get('answerText'),
            "right_ans_mark": answer_info.get('rightAnsMarkText'),
        }
        
        for opt in options_info.get('options', []):
            q["options"].append({
                "label": opt.get('letterArrText'),
                "text": (opt.get('txtText') or '')[:40],
                "data_isright": opt.get('dataIsRight'),
                "right_mark": opt.get('hasRightAnsMark'),
                "checked": opt.get('inputChecked')
            })
        
        all_questions.append(q)
        
        # 尝试显示答案
        if i == 0:
            try_reveal_answer(driver, i+1)
        
        # 如果不是最后一题，跳到下一题
        if i < count - 1:
            print(f"\n    [→] 跳转到下一题...")
            next_clicked = driver.execute_script("""
                var nextBtn = document.querySelector('.swiper-button-next');
                if (nextBtn && !nextBtn.classList.contains('swiper-button-disabled')) {
                    nextBtn.click();
                    return true;
                }
                return false;
            """)
            if next_clicked:
                time.sleep(2)
            else:
                print("    ⚠️ 下一题按钮不可用")
                break
    
    save_json(all_questions, "15_questions_summary.json")
    
    # 打印总结
    print(f"\n{'='*60}")
    print("  === 题目摘要 ===")
    print(f"{'='*60}")
    for q in all_questions:
        print(f"\n  第{q['num']}题 [{q['type']}]: {q['title']}")
        print(f"    answer-text: {q['answer_text']}")
        print(f"    right_ans_mark: {q['right_ans_mark']}")
        for opt in q['options']:
            marker = "✅" if opt.get('data_isright') == '1' or opt.get('right_mark') else "  "
            print(f"    {marker} {opt['label']}: {opt['text']} | isright={opt['data_isright']} | mark={opt['right_mark']}")


# ============ 工具函数 ============
def save_html(driver, filename):
    """保存页面 HTML"""
    filepath = os.path.join(OUTPUT_DIR, filename)
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(driver.page_source)
    print(f"    💾 已保存: {filepath}")


def save_text(text, filename):
    """保存文本内容"""
    filepath = os.path.join(OUTPUT_DIR, filename)
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(text if text else "")
    print(f"    💾 已保存: {filepath}")


def save_json(data, filename):
    """保存 JSON 数据"""
    filepath = os.path.join(OUTPUT_DIR, filename)
    with open(filepath, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"    💾 已保存: {filepath}")


# ============ 主流程 ============
def main():
    print("=" * 60)
    print("  融智云考练习页面 DOM 结构分析工具")
    print("=" * 60)
    
    driver = create_driver()
    
    try:
        # Step 1: 登录
        login(driver)
        
        # Step 2: 导航到练习页面
        navigate_to_practice(driver)
        
        # Step 3: 等待页面完全加载
        print("\n[等待] 等待练习页面完全加载...")
        time.sleep(5)
        
        # Step 4: 分析多道题目
        analyze_multiple_questions(driver, count=3)
        
        # Step 5: 分析网络数据/Vue数据
        analyze_network_api(driver)
        
        # Step 6: 截图保存
        screenshot_path = os.path.join(OUTPUT_DIR, "screenshot.png")
        driver.save_screenshot(screenshot_path)
        print(f"\n    📸 截图已保存: {screenshot_path}")
        
        print(f"\n{'='*60}")
        print(f"  分析完成！所有输出已保存到: {OUTPUT_DIR}")
        print(f"{'='*60}")
        
        # 保持浏览器打开一会以便查看
        input("\n按 Enter 关闭浏览器...")
        
    except Exception as e:
        print(f"\n❌ 发生错误: {e}")
        import traceback
        traceback.print_exc()
        
        # 保存错误时的页面
        try:
            save_html(driver, "error_page.html")
            driver.save_screenshot(os.path.join(OUTPUT_DIR, "error_screenshot.png"))
        except:
            pass
        
        input("\n按 Enter 关闭浏览器...")
    
    finally:
        driver.quit()


if __name__ == "__main__":
    main()
