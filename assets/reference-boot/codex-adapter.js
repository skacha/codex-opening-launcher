/* Codex 本机接入层。原版布局、CSS 动画与五步授权顺序不在这里重绘。 */
(function () {
  'use strict'
  var host = window.__CODEX_HOST_CONFIG__ || {}
  var palette = host.palette || {black:'#120C1B',purple:'#C4BBDB',white:'#F9F5FA'}
  window.__DSH_STARTUP__ = Object.assign({
    mode: 'always', theme: 'light', speed: 1, requireInteraction: true,
    allowSkip: true, sound: true, soundVolume: 0.5, sfx: 'synth',
    voice: true, voiceSource: 'tts', voiceLang: 'en', voiceVolume: 1,
    identity: 'JOYCE MOORE', identityId: '0087', accessLevel: '3',
    brand1: 'CODEX', brand2: 'SYNTHESIZE INTELLIGENCE', brand3a: 'CODEX', brand3b: 'OS',
    orgZh: 'CODEX', orgEn: 'CODEX', orgSub: 'OPENAI CODEX',
    welcomeBox: 'CODEX', footer: 'POWERED BY CODEX',
    accent:palette.purple,glitchA:palette.purple,glitchB:palette.white
  }, host)
  function post(event, details) {
    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(Object.assign({event:event},details||{}))
  }
  window.addEventListener('error', function (e) { post('failure',{message:String(e.message)}) })
  window.addEventListener('unhandledrejection', function () { post('failure',{message:'Animation promise rejected'}) })

  window.__CODEX_REFERENCE_ADAPTER__ = function (api) {
    var ui=api.ui, root=api.root, C=api.config, activeVoice=null, settleVoice=null, step=0
    var confirmed={}, frameCount=0, lastFrame=0, intervals=[]
    // 只替换品牌标识，保留原来的 SVG 容器、勾线组装与落墨动画。
    var mark='M7 10 L23 24 L7 38 L11 42 L32 24 L11 6 Z M30 37 H47 V42 H30 Z'
    ;[ui.blackInner,ui.logo,ui.w3].forEach(function (container) {
      var svg=container.querySelector('svg'),path=svg.querySelector('path')
      if(path)path.setAttribute('d',mark)
      svg.setAttribute('viewBox','2 -1 50 50')
    })
    ui.logo.querySelector('.dsu-logolabel').textContent='CODEX'
    ui.w3.querySelector('.dsu-w3label').textContent='CODEX'
    ui.meta1.textContent='REV 0.1.0 · WEB PROFILE'
    root.style.setProperty('--dsu-ink',palette.black)
    root.style.setProperty('--dsu-box-bg',palette.black)
    root.style.setProperty('--dsu-box-ink',palette.white)
    ui.w4.textContent='欢迎 '+C.identity+' 访问'

    // 同一张透明角色素材派生线稿与彩色层，避免上色时五官、衣饰和轮廓跳位。
    var dragon=document.createElement('div')
    dragon.className='codex-dragon';dragon.setAttribute('aria-hidden','true')
    dragon.innerHTML='<svg class="codex-dragon-defs" xmlns="http://www.w3.org/2000/svg" aria-hidden="true"><defs>'+
      '<filter id="codex-dragon-ink" x="0" y="0" width="100%" height="100%" color-interpolation-filters="sRGB">'+
      '<feColorMatrix in="SourceGraphic" type="luminanceToAlpha" result="luminance"/>'+
      '<feComponentTransfer in="luminance" result="ink"><feFuncA type="table" tableValues="1 .95 .55 .08 0 0"/></feComponentTransfer>'+
      '<feFlood flood-color="'+palette.black+'" result="inkColor"/>'+
      '<feComposite in="inkColor" in2="ink" operator="in" result="lines"/>'+
      '<feComposite in="lines" in2="SourceAlpha" operator="in"/></filter></defs></svg>'+
      '<img class="codex-dragon-line" src="../dragon/dragon-front-v1.png" alt="" decoding="async" draggable="false">'+
      '<img class="codex-dragon-color" src="../dragon/dragon-front-v1.png" alt="" decoding="async" draggable="false">'
    root.insertBefore(dragon,ui.grain||ui.black)

    // 核验时让圆弧避让实际文字包围框，逐字动画和窗口缩放期间也保留阅读净空。
    var ns='http://www.w3.org/2000/svg',reticleSvg=ui.reticle.querySelector('svg')
    var drawing=document.createElementNS(ns,'g'),defs=document.createElementNS(ns,'defs')
    var textMask=document.createElementNS(ns,'mask'),maskBase=document.createElementNS(ns,'rect'),textGap=document.createElementNS(ns,'rect')
    while(reticleSvg.firstChild)drawing.appendChild(reticleSvg.firstChild)
    textMask.setAttribute('id','codex-text-clearance');textMask.setAttribute('maskUnits','userSpaceOnUse')
    textMask.setAttribute('x','0');textMask.setAttribute('y','0');textMask.setAttribute('width','400');textMask.setAttribute('height','400')
    maskBase.setAttribute('width','400');maskBase.setAttribute('height','400');maskBase.setAttribute('fill','white')
    textGap.setAttribute('fill','black');textGap.setAttribute('width','0');textGap.setAttribute('height','0')
    textMask.appendChild(maskBase);textMask.appendChild(textGap);defs.appendChild(textMask)
    drawing.setAttribute('mask','url(#codex-text-clearance)');reticleSvg.appendChild(defs);reticleSvg.appendChild(drawing)
    var gapState=''
    function keepTextClear(){
      if(root.getAttribute('data-codex-step')!=='5'){if(gapState){textGap.setAttribute('width','0');gapState=''}return}
      var ring=reticleSvg.getBoundingClientRect(),title=ui.status.getBoundingClientRect(),subtitle=ui.subEn.getBoundingClientRect()
      if(!ring.width||!ring.height)return
      var pad=12,left=Math.min(title.left,subtitle.left)-pad,right=Math.max(title.right,subtitle.right)+pad
      var top=Math.min(title.top,subtitle.top)-pad,bottom=Math.max(title.bottom,subtitle.bottom)+pad
      var values=[(left-ring.left)*400/ring.width,(top-ring.top)*400/ring.height,(right-left)*400/ring.width,(bottom-top)*400/ring.height].map(function(n){return n.toFixed(3)})
      var state=values.join('/');if(state===gapState)return;gapState=state
      ;['x','y','width','height'].forEach(function(name,i){textGap.setAttribute(name,values[i])})
    }

    // 使用本地生成的英文机械声，不读取上游第三方录音。
    api.voice.line=function (en,zh,name) {
      if(!name || !C.voice) return Promise.resolve(0)
      var index=Number(name.replace('line-',''))-1
      var duration=(host.voiceLengths||[])[index]||0
      if(host.capture) return Promise.resolve(duration*1000)
      if(host.record) return new Promise(function(resolve){post('voice-start',{index:index});setTimeout(function(){post('voice-end',{index:index,completed:true});resolve(duration*1000)},duration*1000)})
      return new Promise(function (resolve) {
        var ended=false, opened=false, timeout
        var media=new Audio('../reference-voice/cue-'+index+'.wav')
        activeVoice=media; media.volume=Math.max(0,Math.min(1,C.voiceVolume))
        function settle(completed) {
          if(ended)return;ended=true;clearTimeout(timeout)
          ui.voice.classList.remove('dsu-on');post('voice-end',{index:index,completed:completed})
          activeVoice=null;settleVoice=null;resolve(duration*1000)
        }
        settleVoice=function(){media.pause();settle(false)}
        media.addEventListener('playing',function(){if(!opened){opened=true;post('voice-start',{index:index})}ui.voice.classList.add('dsu-on')})
        media.addEventListener('ended',function(){settle(true)})
        media.addEventListener('error',function(){settle(false)})
        timeout=setTimeout(function(){media.pause();settle(false)},Math.max(8000,duration*1000+5000))
        media.play().catch(function(){settle(false)})
      })
    }
    api.voice.stop=function(){if(settleVoice)settleVoice()}
    // 由原版状态行的变化记录阶段，交互仍由原版 waitAction 接收。
    function announce() {
      var match=ui.stepcount.textContent.match(/\[\s*(\d+)/)
      var next=match?Number(match[1]):0
      if(root.getAttribute('data-codex-step')!==String(next))root.setAttribute('data-codex-step',String(next))
      if(next!==step){step=next;post('phase',{step:step})}
      if(next>=3&&ui.idline.classList.contains('dsu-in'))ui.idline.classList.remove('dsu-in')
      if(C.requireInteraction&&step>0&&step<3&&ui.action.classList.contains('dsu-in')&&!confirmed['wait'+step]) {
        confirmed['wait'+step]=true;post('waiting',{step:step})
      }
    }
    new MutationObserver(announce).observe(root,{subtree:true,attributes:true,attributeFilter:['class'],characterData:true,childList:true})
    function afterInput(e) {
      if(e.type==='keydown'&&e.key!=='Enter'&&e.key!==' '&&e.key!=='Spacebar')return
      var current=step
      if(current<1||current>2||confirmed[current])return
      if(e.type==='click'&&!ui.action.contains(e.target))return
      setTimeout(function(){
        if(!ui.action.classList.contains('dsu-in')&&!confirmed[current]){confirmed[current]=true;post('confirmed',{step:current})}
      },0)
    }
    document.addEventListener('click',afterInput,true)
    document.addEventListener('keydown',afterInput,true)
    function count(stamp) {
      keepTextClear()
      if(lastFrame&&intervals.length<8192)intervals.push(stamp-lastFrame)
      lastFrame=stamp;frameCount++;window.requestAnimationFrame(count)
    }
    window.requestAnimationFrame(count)
    function stats(){var sorted=intervals.slice().sort(function(a,b){return a-b});return{frames:frameCount,p95FrameMs:sorted.length?sorted[Math.floor((sorted.length-1)*0.95)]:0,renderer:'WebView2 / upstream CSS'}}
    window.__CODEX_FINISH__=function(skipped){post('complete',{skipped:!!skipped,statistics:stats()})}

    // 离屏导出只调原版 DOM 与原版 CSS 动画，不展示额外设计的预览界面。
    window.__CODEX_CAPTURE__={
      seek:function(index,seconds){
        api.stopHex();api.releaseAll()
        root.setAttribute('data-codex-step',String(index))
        root.classList.remove('dsu-boot','dsu-gone','dsu-handoff','dsu-peak')
        ui.blackInner.className='dsu-black-inner dsu-in'
        ui.blackEn.classList.add('dsu-tighten')
        ui.black.style.display='';ui.black.style.opacity=index===0?'1':'0'
        ui.plate.style.opacity=index===0?'0':'1';ui.watermark.style.opacity=index===0?'0':'1'
        ui.wash.style.opacity='0';ui.flash.style.opacity='0'
        ;[ui.brand,ui.foot,ui.stepcount,ui.idline,ui.frame,ui.slats,ui.meta,ui.vignette,ui.voice,ui.skip].forEach(function(x){x.classList.toggle('dsu-in',index!==0)})
        ui.voice.classList.toggle('dsu-on',index>0&&index<6)
        ui.center.classList.toggle('dsu-off',index===0||index===6)
        ui.idline.classList.toggle('dsu-in',index>0&&index<3)
        ui.welcome.classList.toggle('dsu-in',index===6)
        ui.reticle.classList.toggle('dsu-in',index===5)
        ui.logo.classList.toggle('dsu-in',index===3||index===4)
        ui.dots.hidden=index!==3&&index!==4
        ui.action.classList.remove('dsu-in');ui.hex.classList.remove('dsu-in')
        ui.meta2.textContent='13:01:35'
        if(index>0&&index<6){
          var s=api.steps[index-1]
          ui.status.classList.add('dsu-zhline','dsu-in');api.typeIn(ui.statusZh,s.zh,46)
          ui.subEn.classList.add('dsu-in');api.typeIn(ui.subEn,s.en,13);ui.rule.classList.add('dsu-in')
          ui.caret.classList.toggle('dsu-on',index<3)
          ui.subZh.textContent=s.log;ui.subEn2.textContent=s.logEn
          ui.subZh.classList.add('dsu-in');ui.subEn2.classList.add('dsu-in')
          api.setStepCount(index,s.badge);api.setSlats(index);api.setProgress(s.progress)
          if(index<3)api.showAction(s)
          if(index===4){ui.hex.classList.add('dsu-in');ui.hex.textContent='000000  43 4F 44 45 58 20 4C 4F\n000010  41 43 43 45 53 53 20 4F\n000020  50 45 52 41 54 4F 52 20'}
        }else if(index===6){
          api.setStepCount(6,'WELCOME');api.setSlats(6);api.setProgress(1)
          ui.subZh.textContent='身份验证完成，会话已建立';ui.subEn2.textContent='IDENTITY VERIFIED · SESSION ESTABLISHED'
          ui.subZh.classList.add('dsu-in');ui.subEn2.classList.add('dsu-in');ui.idline.classList.remove('dsu-in')
        }
        // 固定导出时刻的动画相位；实际播放继续使用浏览器原生合成动画。
        void root.offsetWidth
        document.getAnimations().forEach(function(a){a.pause();a.currentTime=seconds*1000})
        return stats()
      },
      stop:function(){api.releaseAll();api.stopHex();api.voice.stop()}
    }
    post('ready',{capture:!!host.capture,statistics:stats()})
  }
})()
