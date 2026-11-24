using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CodexOpening
{
    // 浅色工业终端开场；保留旧类名以兼容播放、配置和多屏接入。
    // 进度与核验信息仅用于演出，实际启动仍由原生确认按钮控制。
    public sealed class EvaBootScene : FrameworkElement
    {
        private static readonly Brush Paper=Paint("#F1F2EE"), Ink=Paint("#171D20"), Accent=Paint("#007F93"), White=Paint("#FFFFFF");
        private static readonly Brush Muted=Paint("#667177"), Rule=Paint("#CED4D1"), Pale=Paint("#E6EAE6");
        private static readonly Typeface Mono=new Typeface("Consolas"), Display=new Typeface(new FontFamily("Bahnschrift"),FontStyles.Normal,FontWeights.Bold,FontStretches.Normal);
        private static readonly string[] Phases={"BOOT","INTERFACE","SYNCHRONIZE","VERIFY","WELCOME"};
        private readonly Dictionary<string,FormattedText> texts=new Dictionary<string,FormattedText>();
        private readonly Dictionary<string,Pen> pens=new Dictionary<string,Pen>();
        private readonly Stopwatch animation=new Stopwatch();
        private readonly List<double> intervals=new List<double>();
        private readonly DrawingGroup stationery=new DrawingGroup();
        private double target,progress,seconds,previewSeconds=8,lastTime,lastComposition=-1,drawMilliseconds,peakDrawMilliseconds;
        private int frames;
        private bool subscribed,paused,confirmationReady;

        public EvaBootScene()
        {
            IsHitTestVisible=false;
            using(DrawingContext g=stationery.Open())DrawStationery(g);
            stationery.Freeze();
            Loaded+=delegate{StartAnimation();};Unloaded+=delegate{StopAnimation();};
        }
        public void SetProgress(double value)
        {target=Math.Max(0,Math.Min(1,value));if(!subscribed){progress=target;seconds=previewSeconds;InvalidateVisual();}}
        public void SetPreviewTime(double value)
        {previewSeconds=seconds=Math.Max(0,value);progress=target;InvalidateVisual();}
        public void SetConfirmationReady(){confirmationReady=true;InvalidateVisual();}
        public void SetPaused(bool value)
        {paused=value;lastComposition=-1;if(value)animation.Stop();else if(subscribed){lastTime=animation.Elapsed.TotalSeconds;animation.Start();}}
        private void StartAnimation()
        {if(subscribed)return;subscribed=true;animation.Start();lastTime=animation.Elapsed.TotalSeconds;CompositionTarget.Rendering+=Animate;}
        public void StopAnimation()
        {if(!subscribed)return;CompositionTarget.Rendering-=Animate;subscribed=false;animation.Stop();}
        private void Animate(object sender,EventArgs e)
        {
            var rendering=e as RenderingEventArgs;
            if(paused||!IsVisible||rendering==null||rendering.RenderingTime.TotalSeconds==lastComposition)return;
            double stamp=rendering.RenderingTime.TotalSeconds;
            if(lastComposition>=0&&intervals.Count<4096)intervals.Add((stamp-lastComposition)*1000);
            lastComposition=stamp;seconds=animation.Elapsed.TotalSeconds;
            double dt=Math.Min(0.05,Math.Max(0,seconds-lastTime));lastTime=seconds;
            progress+=(target-progress)*(1-Math.Exp(-dt*14));InvalidateVisual();
        }
        public object Statistics()
        {
            double[] sorted=intervals.ToArray();Array.Sort(sorted);
            return new{frames=frames,meanDrawMs=frames==0?0:drawMilliseconds/frames,peakDrawMs=peakDrawMilliseconds,
                p95FrameMs=sorted.Length==0?0:sorted[(int)((sorted.Length-1)*0.95)],renderTier=RenderCapability.Tier>>16,cachedTexts=texts.Count};
        }
        protected override void OnRender(DrawingContext g)
        {
            long begin=Stopwatch.GetTimestamp();base.OnRender(g);
            g.DrawRectangle(Paper,null,new Rect(0,0,ActualWidth,ActualHeight));if(ActualWidth<=0||ActualHeight<=0)return;
            double scale=Math.Min(ActualWidth/1600,ActualHeight/900);
            g.PushTransform(new TranslateTransform((ActualWidth-1600*scale)/2,(ActualHeight-900*scale)/2));g.PushTransform(new ScaleTransform(scale,scale));
            g.PushClip(new RectangleGeometry(new Rect(0,0,1600,900)));g.DrawDrawing(stationery);
            double position=progress*5;int phase=Math.Min(4,(int)position);double local=Math.Min(1,position-phase);
            DrawTelemetry(g,phase);DrawOrbit(g);
            double reveal=Ease(local/0.12);
            // 保留贯穿全程的圆弧和网格；短距离遮罩显露正文，避免整页翻转。
            g.PushClip(new RectangleGeometry(new Rect(280,245,1040,419)));
            if(phase>0&&reveal<1){g.PushOpacity(1-reveal);g.PushTransform(new TranslateTransform(0,-10*reveal));DrawPhase(g,phase-1,1);g.Pop();g.Pop();}
            g.PushOpacity(phase==0?Ease(seconds/0.7):reveal);
            g.PushTransform(new TranslateTransform(0,phase==0?0:14*(1-reveal)));DrawPhase(g,phase,local);g.Pop();g.Pop();g.Pop();
            DrawTimeline(g,phase);
            g.Pop();g.Pop();g.Pop();
            double elapsed=(Stopwatch.GetTimestamp()-begin)*1000.0/Stopwatch.Frequency;frames++;drawMilliseconds+=elapsed;peakDrawMilliseconds=Math.Max(peakDrawMilliseconds,elapsed);
        }
        private void DrawStationery(DrawingContext g)
        {
            g.DrawRectangle(Paper,null,new Rect(0,0,1600,900));
            // 极浅竖向分区与底纹，保持参考画面的大留白。
            g.PushOpacity(0.37);g.DrawRectangle(White,null,new Rect(1014,0,184,900));g.DrawRectangle(Pale,null,new Rect(1220,0,42,900));g.Pop();
            for(int x=64;x<=1536;x+=184)Line(g,x,0,x,819,Rule,0.45);
            Line(g,64,210,1536,210,Rule,0.8);Line(g,64,780,1536,780,Rule,0.8);
            Label(g,"CODEX",64,44,49,Ink,true);Label(g,"SYNTHESIZE / BUILD / EXPLORE",67,105,10,Ink);
            Label(g,"COMMAND OS",64,125,29,Ink,true);Label(g,"REV 01.0 / LOCAL CONSOLE",65,181,11,Muted);
            Label(g,"SECURE SESSION",1365,62,11,Muted);Label(g,"01 / EN-US",1390,86,16,Ink);
            Line(g,1358,119,1535,119,Ink,2);g.DrawRectangle(Accent,null,new Rect(1499,114,36,5));
            Label(g,"OPERATOR CONSOLE",65,236,10,Muted);Label(g,"C / 01",1432,236,11,Muted);
            Cross(g,64,210);Cross(g,1536,210);Cross(g,64,780);Cross(g,1536,780);
            Label(g,"SEQUENCE / LOCAL",64,816,11,Muted);Label(g,"POWERED BY CODEX",1331,818,12,Ink,true);
            Line(g,1509,839,1536,839,Ink,4);Label(g,"VISUAL BOOT SEQUENCE",64,845,9,Muted);
        }
        private void DrawTelemetry(DrawingContext g,int phase)
        {
            for(int i=0;i<5;i++)g.DrawRectangle(i<=phase?Accent:Rule,null,new Rect(65+i*28,201,22,3));
            Label(g,"ELAPSED /",1250,184,10,Muted);Number(g,seconds.ToString("00.0",CultureInfo.InvariantCulture),1332,179,16,Ink);Label(g,"SEC",1381,184,10,Muted);
            Label(g,"STATUS / "+(confirmationReady?"READY":"ACTIVE"),65,269,11,Accent);Label(g,"LOCAL HOST",1372,269,11,Muted);
            Line(g,1373,292,1536,292,Rule,1);
            Label(g,phase>=1?"INTERFACE / OK":"INTERFACE / --",1373,307,10,phase>=1?Accent:Muted);
            Label(g,phase>=3?"WORKSPACE / OK":"WORKSPACE / --",1373,330,10,phase>=3?Accent:Muted);
            Label(g,confirmationReady?"SESSION / READY":"SESSION / WAIT",1373,353,10,confirmationReady?Accent:Muted);
            // 细小信号柱和移动刻度在等待确认时仍然运行。
            for(int i=0;i<13;i++){double h=5+12*(0.5+0.5*Math.Sin(seconds*2.2+i*0.83));g.DrawRectangle(i%4==0?Accent:Muted,null,new Rect(67+i*5,750-h,2,h));}
            double sweep=270+(seconds*37)%470;
            g.PushOpacity(0.32);Line(g,63,sweep,74,sweep,Accent,1);Line(g,1526,sweep,1537,sweep,Accent,1);g.Pop();
            Label(g,"ID / CX-0001",65,718,10,Muted);Label(g,"DEMONSTRATION TELEMETRY",1301,751,9,Muted);
        }
        private void DrawOrbit(DrawingContext g)
        {
            double swell=Ease((progress-0.29)/0.13)*(1-Ease((progress-0.77)/0.1)),radius=104+192*swell;
            g.PushOpacity(0.12+0.62*swell);
            // 所有阶段共用连续时钟，圆弧无跳变地扩张、旋转、收拢。
            Arc(g,800,444,radius,seconds*15-100,264,Ink,5);
            Arc(g,800,444,radius-28,-seconds*22+26,118,Accent,1.3);Arc(g,800,444,radius-65,seconds*10+110,206,Ink,1.4);
            g.DrawEllipse(null,Pen(White,2),new Point(800,444),radius-13,radius-13);
            for(int i=0;i<3;i++){double a=(seconds*14+i*120)*Math.PI/180;g.DrawEllipse(Accent,null,new Point(800+(radius-28)*Math.Cos(a),444+(radius-28)*Math.Sin(a)),3.5,3.5);}
            g.Pop();
            if(swell>0.01)
            {
                g.PushOpacity(swell*0.6);
                for(int i=0;i<60;i++){double a=i*Math.PI/30,r=radius+15;Line(g,800+r*Math.Cos(a),444+r*Math.Sin(a),800+(r+(i%5==0?9:3))*Math.Cos(a),444+(r+(i%5==0?9:3))*Math.Sin(a),i%5==0?Muted:Rule,0.8);}
                g.Pop();
            }
        }
        private void DrawPhase(DrawingContext g,int phase,double local)
        {if(phase==0)DrawBoot(g,local);else if(phase==1)DrawInterface(g,local);else if(phase==2)DrawSync(g,local);else if(phase==3)DrawVerify(g,local);else DrawWelcome(g,local);}
        private void DrawBoot(DrawingContext g,double local)
        {
            Spaced(g,"COMMAND SYSTEM",800,293,12,Muted);CenterAt(g,"CODEX",800,347,106,Ink,true);
            g.DrawRectangle(Ink,null,new Rect(601,462,398,36));Spaced(g,"BOOT SEQUENCE INITIATED",800,470,14,Paper);
            CenterAt(g,"LOCAL INTELLIGENCE / READY TO CONNECT",800,535,11,Muted,false);RevealLine(g,650,584,300,Ease(local),Accent);
            CenterAt(g,"INITIALIZING COMMAND ENVIRONMENT",800,611,10,Accent,false);
        }
        private void DrawInterface(DrawingContext g,double local)
        {
            g.PushOpacity(0.96);g.DrawRectangle(Paper,null,new Rect(526,279,548,357));g.Pop();
            Spaced(g,"IDENTITY / INTERFACE",800,291,12,Muted);CenterAt(g,"ACCESS REQUEST",800,339,47,Ink,true);
            Spaced(g,"COMMAND INTERFACE CONNECTED",800,402,10,Accent);Line(g,565,451,1035,451,Rule,1);
            Label(g,"OPERATOR",580,475,12,Muted);Label(g,"CODEX USER",835,471,20,Ink,true);
            Label(g,"CONSOLE ID",580,515,12,Muted);Label(g,"CX-0001",835,513,16,Ink);
            Label(g,"ACCESS SCOPE",580,552,12,Muted);Label(g,"LOCAL WORKSPACE",835,549,16,Ink);
            Line(g,565,590,1035,590,Rule,1);CenterAt(g,local>0.70?"INTERFACE VERIFIED":"ESTABLISHING CONNECTION",800,617,11,Accent,false);
        }
        private void DrawSync(DrawingContext g,double local)
        {
            // 中央横带跨越圆环，形成参考中的信息压印层次。
            g.PushOpacity(0.97);g.DrawRectangle(Paper,null,new Rect(426,374,748,134));g.Pop();
            CenterAt(g,"WORKSPACE LINK",800,380,47,Ink,true);Spaced(g,"SYNCHRONIZING",800,448,12,Accent);
            Number(g,(Ease(local)*100).ToString("000.0",CultureInfo.InvariantCulture),743,541,29,Ink);Label(g,"%",837,553,12,Muted);
            RevealLine(g,619,596,362,Ease(local),Accent);CenterAt(g,"CONTEXT / TOOLS / COMMAND CHANNEL",800,627,10,Muted,false);
        }
        private void DrawVerify(DrawingContext g,double local)
        {
            bool done=local>0.43;g.PushOpacity(0.97);g.DrawRectangle(Paper,null,new Rect(407,374,786,138));g.Pop();
            CenterAt(g,done?"READ COMPLETE":"SYSTEM VERIFICATION",800,380,44,Ink,true);Spaced(g,done?"VERIFIED":"CONSENSUS CHECK",800,448,12,Accent);
            string[] labels={"INTERFACE","WORKSPACE","COMMAND"};
            for(int i=0;i<3;i++){double x=554+i*172;bool ack=local>0.10+i*0.13;g.DrawRectangle(ack?Accent:Rule,null,new Rect(x,565,12,12));Label(g,labels[i],x+22,564,10,Ink);}
            CenterAt(g,"ALL SYSTEMS NOMINAL / SESSION PREPARED",800,625,11,Muted,false);
        }
        private void DrawWelcome(DrawingContext g,double local)
        {
            Spaced(g,"WELCOME TO",800,288,27,Ink);g.DrawRectangle(Ink,null,new Rect(604,339,392,67));CenterAt(g,"CODEX",800,340,52,Paper,true);
            // 使用终端符号而非参考中的鲸鱼标识。
            double reveal=Ease(local/0.24);g.PushOpacity(reveal);g.PushTransform(new TranslateTransform(0,10*(1-reveal)));
            Line(g,767,438,789,455,Ink,5);Line(g,789,455,767,472,Ink,5);Line(g,804,474,833,474,Accent,5);g.Pop();g.Pop();
            Spaced(g,"COMMAND ACCESS",800,519,10,Muted);CenterAt(g,"YOUR WORKSPACE AWAITS",800,550,21,Ink,true);
            CenterAt(g,confirmationReady?"SESSION PREPARED / CONFIRM TO ENTER":"VOICE CHANNEL / FINAL CHECK",800,601,11,Accent,false);
            // 原生按钮位于 700 至 752；正文不占用点击区域。
            CenterAt(g,confirmationReady?"PRESS ENTER OR SELECT ENTER CODEX":"PLEASE WAIT FOR THE VOICE CHANNEL",800,645,10,Muted,false);
        }
        private void DrawTimeline(DrawingContext g,int phase)
        {
            double full=progress*5;
            for(int i=0;i<5;i++){double x=434+i*148;Line(g,x,800,x+128,800,Rule,2);double fill=Math.Max(0,Math.Min(1,full-i));if(fill>0)Line(g,x,800,x+128*fill,800,Accent,2);Label(g,"0"+(i+1),x,814,10,i<=phase?Ink:Muted);Label(g,Phases[i],x,834,9,i==phase?Accent:Muted);}
            // 底部所有动态文本按字符缓存，避免每帧创建新的数字排版。
            Number(g,Math.Min(100,progress*100).ToString("000",CultureInfo.InvariantCulture),1228,816,17,Ink);Label(g,"%",1264,822,10,Muted);
        }
        private void RevealLine(DrawingContext g,double x,double y,double width,double fill,Brush color)
        {Line(g,x,y,x+width,y,Rule,1);if(fill>0)Line(g,x,y,x+width*fill,y,color,2);g.DrawRectangle(color,null,new Rect(x+width*fill-2,y-3,4,6));}
        private void Cross(DrawingContext g,double x,double y){Line(g,x-5,y,x+5,y,Muted,0.8);Line(g,x,y-5,x,y+5,Muted,0.8);}
        private void Arc(DrawingContext g,double x,double y,double radius,double angle,double length,Brush color,double width)
        {
            double a=angle*Math.PI/180,b=(angle+length)*Math.PI/180;
            var path=new StreamGeometry();using(var c=path.Open()){c.BeginFigure(new Point(x+radius*Math.Cos(a),y+radius*Math.Sin(a)),false,false);c.ArcTo(new Point(x+radius*Math.Cos(b),y+radius*Math.Sin(b)),new Size(radius,radius),0,length>180,SweepDirection.Clockwise,true,false);}path.Freeze();g.DrawGeometry(null,Pen(color,width),path);
        }
        private static double Ease(double n){n=Math.Max(0,Math.Min(1,n));return n*n*(3-2*n);}
        private static Brush Paint(string hex){var b=(SolidColorBrush)new BrushConverter().ConvertFromString(hex);b.Freeze();return b;}
        private Pen Pen(Brush color,double width)
        {string key=color.ToString()+"/"+width.ToString(CultureInfo.InvariantCulture);Pen pen;if(!pens.TryGetValue(key,out pen)){pen=new Pen(color,width);pen.Freeze();pens[key]=pen;}return pen;}
        private void Line(DrawingContext g,double x1,double y1,double x2,double y2,Brush color,double width){g.DrawLine(Pen(color,width),new Point(x1,y1),new Point(x2,y2));}
        private void Label(DrawingContext g,string text,double x,double y,double size,Brush color,bool title=false){g.DrawText(Text(text,size,color,title),new Point(x,y));}
        private void CenterAt(DrawingContext g,string text,double x,double y,double size,Brush color,bool title){var t=Text(text,size,color,title);g.DrawText(t,new Point(x-t.Width/2,y));}
        private void Spaced(DrawingContext g,string text,double x,double y,double size,Brush color)
        {double step=size*0.76;for(int i=0;i<text.Length;i++)Label(g,text[i].ToString(),x-(text.Length-1)*step/2-size*0.30+i*step,y,size,color);}
        private void Number(DrawingContext g,string value,double x,double y,double size,Brush color)
        {for(int i=0;i<value.Length;i++)Label(g,value[i].ToString(),x+i*size*0.6001,y,size,color);}
        private FormattedText Text(string text,double size,Brush color,bool title)
        {
            string key=text+"/"+size.ToString(CultureInfo.InvariantCulture)+"/"+color.ToString()+"/"+title;FormattedText t;
            if(!texts.TryGetValue(key,out t)){if(texts.Count>=384)texts.Clear();t=new FormattedText(text,CultureInfo.GetCultureInfo("en-US"),FlowDirection.LeftToRight,title?Display:Mono,size,color,1.0);texts[key]=t;}return t;
        }
    }
}
