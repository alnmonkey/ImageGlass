!function(e,t){"object"==typeof exports&&"object"==typeof module?module.exports=t():"function"==typeof define&&define.amd?define("ig-ui",[],t):"object"==typeof exports?exports["ig-ui"]=t():e["ig-ui"]=t()}(this,(()=>(()=>{"use strict";var e={r:e=>{"undefined"!=typeof Symbol&&Symbol.toStringTag&&Object.defineProperty(e,Symbol.toStringTag,{value:"Module"}),Object.defineProperty(e,"__esModule",{value:!0})}},t={};e.r(t);var n=function(e){queryAll(".tab-page").forEach((function(e){return e.classList.remove("active")}));var t=query('.tab-page[tab="'.concat(e,'"]'));null==t||t.classList.add("active"),queryAll('input[type="radio"]').forEach((function(e){return e.checked=!1}));var n=query('input[type="radio"][value="'.concat(e,'"]'));n&&(n.checked=!0)},r=function(){for(var e in _pageSettings.lang)if(Object.prototype.hasOwnProperty.call(_pageSettings.lang,e))for(var t=_pageSettings.lang[e],n=0,r=queryAll('[data-lang="'.concat(e,'"]'));n<r.length;n++){r[n].innerText=t}},o=function(){for(var e in function(){var e=function(e){if(!Object.prototype.hasOwnProperty.call(_pageSettings.enums,e))return"continue";for(var t=_pageSettings.enums[e],n=function(n){t.forEach((function(t){var r=new Option("".concat(t),t);r.setAttribute("data-lang","_.".concat(e,"._").concat(t)),n.add(r)}))},r=0,o=queryAll('select[data-enum="'.concat(e,'"]'));r<o.length;r++)n(o[r])};for(var t in _pageSettings.enums)e(t)}(),_pageSettings.config)if(Object.prototype.hasOwnProperty.call(_pageSettings.config,e)){var t=_pageSettings.config[e];if("string"==typeof t||"number"==typeof t||"boolean"==typeof t){var n=query('[name="'.concat(e,'"]'));if(n){var r=n.tagName.toLowerCase();if("select"===r)n.value=t.toString();else if("input"===r){var o=n.getAttribute("type").toLowerCase(),a=n;if("radio"===o||"checkbox"===o)a.checked=Boolean(t);else if("color"===o){var i=t.toString()||"#00000000";a.value=i.substring(0,i.length-2)}else a.value=t.toString()}}}}query("#Lnk_StartupDir").innerText=_pageSettings.startUpDir||"(unknown)",query("#Lnk_ConfigDir").innerText=_pageSettings.configDir||"(unknown)",query("#Lnk_UserConfigFile").innerText=_pageSettings.userConfigFilePath||"(unknown)"};window.query=function(e){try{return document.querySelector(e)}catch(e){}return null},window.queryAll=function(e){try{return Array.from(document.querySelectorAll(e))}catch(e){}return[]},window._pageSettings||(window._pageSettings={config:{},lang:{},enums:{ImageOrderBy:[],ImageOrderType:[],ColorProfileOption:[],AfterEditAppAction:[],ImageInterpolation:[],MouseWheelAction:[],MouseWheelEvent:[],MouseClickEvent:[],BackdropStyle:[],ToolbarItemModelType:[]},startUpDir:"",configDir:"",userConfigFilePath:""}),_pageSettings.setActiveTab=n,_pageSettings.loadLanguage=r,_pageSettings.loadSettings=o;for(var a=Array.from(document.querySelectorAll('input[name="nav"]')),i=0;i<a.length;i++){a[i].addEventListener("change",(function(e){var t=e.target.value;n(t)}),!1)}return n("image"),o(),r(),t})()));