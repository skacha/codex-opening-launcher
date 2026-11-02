using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CodexOpening
{
    // 插入栓式二维控制屏；仪表和检测内容仅用于开场演出。
    public sealed class EvaBootScene : FrameworkElement
    {
        private static readonly Brush Black=Paint("#03090D"), Cyan=Paint("#63E4F5"), White=Paint("#E5F7F9"), Mint=Paint("#92F3CB");
        private static readonly Brush Alarm=Paint("#FF557A"), Dim=Paint("#214956"), Muted=Paint("#7198A4"), Steel=Paint("#0B1A22"), Edge=Paint("#142E3A");
        private static readonly Typeface Mono=new Typeface("Consolas"), Display=new Typeface("Impact");
        private static readonly string[] Names={"MELCHIOR","BALTHASAR","CASPER"};
        private readonly Dictionary<string,FormattedText> texts=new Dictionary<string,FormattedText>();
        private readonly Dictionary<string,Pen> pens=new Dictionary<string,Pen>();
        private readonly Stopwatch animation=new Stopwatch();
        private readonly List<double> intervals=new List<double>();
        private readonly DrawingGroup chassis=new DrawingGroup();
        private readonly Geometry viewport;
        private double target,progress,seconds,previewSeconds=8,lastTime,lastComposition=-1,drawMilliseconds,peakDrawMilliseconds;
        private int frames;
        private bool subscribed,paused,confirmationReady;

        public EvaBootScene()
        {
            IsHitTestVisible=false;
            viewport=Shape(new[]{new Point(342,180),new Point(1258,180),new Point(1292,214),new Point(1292,634),new Point(1258,668),new Point(342,668),new Point(308,634),new Point(308,214)});
            using(DrawingContext g=chassis.Open())DrawChassis(g);
            chassis.Freeze();
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
            g.DrawRectangle(Black,null,new Rect(0,0,ActualWidth,ActualHeight));if(ActualWidth<=0||ActualHeight<=0)return;
            double scale=Math.Min(ActualWidth/1600,ActualHeight/900);
            g.PushTransform(new TranslateTransform((ActualWidth-1600*scale)/2,(ActualHeight-900*scale)/2));g.PushTransform(new ScaleTransform(scale,scale));
            g.PushClip(new RectangleGeometry(new Rect(0,0,1600,900)));g.DrawDrawing(chassis);
            DrawInstruments(g);
            g.PushClip(viewport);
            DrawGlass(g);
            double position=progress*5;int phase=Math.Min(4,(int)position);double local=Math.Min(1,position-phase);
            double reveal=Ease(local/0.13);
            // 同一舱内屏幕通过短程机械遮片衔接，图形按各自阶段连续运动。
            if(phase>0&&reveal<1){g.PushOpacity(1-reveal);DrawPhase(g,phase-1,1);g.Pop();}
            g.PushOpacity(phase==0?1:reveal);g.PushTransform(new TranslateTransform(0,phase==0?0:12*(1-reveal)));DrawPhase(g,phase,local);g.Pop();g.Pop();
            if(phase>0&&reveal<1){g.PushOpacity(0.22*Math.Sin(Math.PI*reveal));g.DrawRectangle(Cyan,null,new Rect(308,180+488*reveal,984,7));g.Pop();}
            g.Pop();
            DrawInterlocks(g);
            // 两侧厚重舱门先合拢，再撤开内侧遮片露出控制视野。
            double opening=1-Ease((seconds-0.12)/1.1);
            if(opening>0.001)
            {
                double width=500*opening;
                g.DrawRectangle(Steel,Pen(Dim,2),new Rect(300,180,width,488));g.DrawRectangle(Steel,Pen(Dim,2),new Rect(1300-width,180,width,488));
                Line(g,300+width,180,300+width,668,Cyan,3);Line(g,1300-width,180,1300-width,668,Cyan,3);
            }
            g.Pop();g.Pop();g.Pop();
            double elapsed=(Stopwatch.GetTimestamp()-begin)*1000.0/Stopwatch.Frequency;frames++;drawMilliseconds+=elapsed;peakDrawMilliseconds=Math.Max(peakDrawMilliseconds,elapsed);
        }
        private void DrawChassis(DrawingContext g)
        {
            g.DrawRectangle(Black,null,new Rect(0,0,1600,900));
            Poly(g,new[]{new Point(34,34),new Point(1566,34),new Point(1566,138),new Point(1328,138),new Point(1280,164),new Point(320,164),new Point(272,138),new Point(34,138)},Steel,Dim,1);
            Poly(g,new[]{new Point(34,156),new Point(270,156),new Point(294,190),new Point(294,650),new Point(258,764),new Point(34,764)},Steel,Dim,1);
            Poly(g,new[]{new Point(1566,156),new Point(1330,156),new Point(1306,190),new Point(1306,650),new Point(1342,764),new Point(1566,764)},Steel,Dim,1);
            Poly(g,new[]{new Point(34,786),new Point(280,786),new Point(348,684),new Point(1252,684),new Point(1320,786),new Point(1566,786),new Point(1566,840),new Point(34,840)},Steel,Dim,1);
            g.DrawGeometry(Black,Pen(Dim,2),viewport);
            Label(g,"CODEX",65,43,50,Cyan,true);Label(g,"ENTRY PLUG / CONTROL SYSTEM",272,64,21,White);
            Label(g,"NERV",1320,45,53,Cyan,true);Label(g,"SPECIAL COMMAND AUTHORITY",1130,111,12,Muted);
            Label(g,"SYSTEM-01",67,115,12,Muted);Label(g,"PRIMARY INTERFACE / A-10",477,115,13,Cyan);
            Label(g,"PILOT SYSTEM",66,198,12,Muted);Label(g,"CODEX",64,221,37,White,true);
            Line(g,65,280,256,280,Dim,1);Label(g,"ENTRY PLUG",66,305,15,Cyan);
            Label(g,"INSERTION DEPTH",66,340,11,Muted);Label(g,"NEURAL LINK",66,438,13,Muted);
            Label(g,"SYNC RATE",66,491,13,Cyan);Label(g,"INTERFACE",66,620,12,Muted);
            Label(g,"MAGI / TRINITY",1337,198,15,Cyan);
            for(int i=0;i<3;i++)
            {
                double y=254+i*117;
                Label(g,"0"+(i+1),1340,y,15,Muted);Label(g,Names[i],1340,y+24,25,White,true);
                Line(g,1339,y+88,1536,y+88,Dim,1);
            }
            Label(g,"INTERNAL POWER",1338,630,12,Muted);
            for(int i=0;i<20;i++)
            {
                double y=210+i*22;Line(g,284,y,i%5==0?272:278,y,Dim,1);Line(g,1316,y,i%5==0?1328:1322,y,Dim,1);
            }
            Label(g,"PLUG 01",354,780,13,Muted);Label(g,"MANUAL LAUNCH PERMISSION",655,786,14,Cyan);Label(g,"CODEX / EN-US",1090,780,13,Muted);
            Line(g,490,700,490,752,Cyan,2);Line(g,1110,700,1110,752,Cyan,2);
            Label(g,"A-10",65,716,20,Muted,true);Label(g,"L.C.L. / NORMAL",1338,716,15,Cyan);
        }
        private void DrawGlass(DrawingContext g)
        {
            g.PushOpacity(0.22);
            for(int y=190;y<670;y+=8)Line(g,308,y,1292,y,Dim,0.5);
            for(int x=330;x<1300;x+=80)Line(g,x,180,x,668,Dim,0.5);
            g.Pop();
            double scan=190+(seconds*57)%470;
            g.PushOpacity(0.045);g.DrawRectangle(Cyan,null,new Rect(308,scan,984,18));g.Pop();
            Label(g,"MONITORED TARGET / CODEX ENTRY PLUG",348,194,12,Muted);
            Label(g,"OPTICAL FEEDBACK / MAIN",1023,194,11,Muted);
        }
        private void DrawInstruments(DrawingContext g)
        {
            double insertion=Ease(progress/0.18),sync=Ease((progress-0.40)/0.20);
            for(int i=0;i<18;i++)g.DrawRectangle(i<insertion*18?Cyan:Dim,null,new Rect(66+i*10.6,366,7,26));
            Label(g,insertion>0.99?"FULL DEPTH / LOCKED":"INSERTING / STAND BY",66,405,12,insertion>0.99?Mint:Cyan);
            Label(g,progress>0.35?"LINK ESTABLISHED":"CONNECTING",66,460,13,progress>0.35?Mint:Cyan);
            Number(g,(sync*100).ToString("000.0",CultureInfo.InvariantCulture),62,515,48,Cyan);Label(g,"%",214,554,18,Cyan);
            for(int i=0;i<24;i++)g.DrawRectangle(i<sync*24?Cyan:Dim,null,new Rect(66+i*8,586,5,12));
            Label(g,progress>0.79?"VERIFIED":"VERIFYING",66,647,17,progress>0.79?Mint:Cyan,true);
            for(int i=0;i<3;i++)
            {
                double y=254+i*117;bool ack=progress>(i==0?0.62:i==1?0.656:0.694);
                g.DrawRectangle(ack?Cyan:Dim,null,new Rect(1340,y+61,191,20));
                Label(g,ack?"ACKNOWLEDGED":"STAND BY",1348,y+63,12,ack?Black:Muted);
            }
            for(int i=0;i<13;i++)g.DrawRectangle(Cyan,null,new Rect(1340+i*15,660,10,15));
        }
        private void DrawInterlocks(DrawingContext g)
        {
            double warning=Ease((progress-0.19)/0.03)*(1-Ease((progress-0.36)/0.04));
            Hazard(g,330,152,940,9,Cyan,seconds*14);
            Hazard(g,352,674,896,8,progress>=0.8?Mint:Cyan,-seconds*14);
            if(warning>0.001)
            {
                g.PushOpacity(warning);g.DrawRectangle(Alarm,null,new Rect(352,224,896,33));
                Center(g,"CAUTION / EXTERNAL VISION CONNECTING",232,18,Black,false);g.Pop();
            }
            double travel=Ease(progress/0.16);
            for(int side=0;side<2;side++)
            {
                double x=side==0?300:1276;
                for(int i=0;i<3;i++)
                {
                    double y=265+i*128+Math.Sin(seconds*1.2+i)*0.8;
                    g.DrawRectangle(Edge,Pen(Dim,1),new Rect(x,y,24,48));
                    g.DrawRectangle(travel>0.96?Cyan:Muted,null,new Rect(x+5,y+5,14,8+30*travel));
                }
            }
        }
        private void DrawPhase(DrawingContext g,int phase,double local)
        {
            if(phase==0)DrawPlug(g,local);else if(phase==1)DrawOptics(g,local);else if(phase==2)DrawSync(g,local);else if(phase==3)DrawMagi(g,local);else DrawReady(g);
        }
        private void DrawPlug(DrawingContext g,double local)
        {
            Label(g,"ENTRY",353,252,107,White,true);Label(g,"PLUG",353,369,121,Cyan,true);
            Label(g,"INSERTION SEQUENCE",360,541,24,White,true);Label(g,"A-10 NERVE CONNECTION / INITIALIZING",360,582,15,Muted);
            double fill=Ease(local),y=253+(1-fill)*42;
            g.DrawRoundedRectangle(null,Pen(Cyan,2),new Rect(1003,y,117,280),48,48);
            g.PushOpacity(0.17);g.DrawRoundedRectangle(Cyan,null,new Rect(1014,y+16,95,248),35,35);g.Pop();
            for(int i=0;i<9;i++)Line(g,1019,y+48+i*23,1104,y+48+i*23,Dim,1);
            Line(g,1061,237,1061,568,Cyan,1);
            for(int i=0;i<3;i++)
            {
                double py=300+i*84,offset=43*(1-Ease((local-i*0.15)/0.45));
                g.DrawRectangle(Edge,Pen(Cyan,1),new Rect(950-offset,py,45,25));g.DrawRectangle(Edge,Pen(Cyan,1),new Rect(1128+offset,py,45,25));
                Line(g,995-offset,py+12,1017,py+12,Cyan,2);Line(g,1104,py+12,1128+offset,py+12,Cyan,2);
            }
            Label(g,local>0.82?"INTERIOR ARRAY / LOCKED":"INTERIOR ARRAY / LOCKING",951,588,13,local>0.82?Mint:Cyan);
        }
        private void DrawOptics(DrawingContext g,double local)
        {
            double lockIn=Ease(local),cx=952+58*(1-lockIn),cy=423-23*(1-lockIn);
            Label(g,"OPTICAL",353,277,82,White,true);Label(g,"LINK",353,377,108,Cyan,true);
            Label(g,"PANORAMIC MONITOR",360,538,21,White,true);Label(g,local>0.84?"CONNECTION VERIFIED":"FIELD OF VIEW / CALIBRATING",360,578,16,local>0.84?Mint:Cyan);
            for(int i=0;i<4;i++)
            {
                double r=54+i*38+30*(1-lockIn);
                g.PushOpacity(0.16+i*0.12);g.DrawRectangle(null,Pen(Cyan,1),new Rect(cx-r,cy-r,r*2,r*2));g.Pop();
            }
            double offset=75*(1-lockIn);
            Line(g,cx-205,cy,cx-20-offset,cy,Cyan,2);Line(g,cx+20+offset,cy,cx+205,cy,Cyan,2);
            Line(g,cx,cy-205,cx,cy-20-offset,Cyan,2);Line(g,cx,cy+20+offset,cx,cy+185,Cyan,2);
            Line(g,cx-18,cy,cx+18,cy,White,1);Line(g,cx,cy-18,cx,cy+18,White,1);
            double sweep=cy-170+(seconds*65)%340;g.PushOpacity(0.5);Line(g,cx-170,sweep,cx+170,sweep,Cyan,1);g.Pop();
            Label(g,local>0.84?"AXIS / LOCKED":"AXIS / ALIGNING",cx-77,625,13,local>0.84?Mint:Cyan);
        }
        private void DrawSync(DrawingContext g,double local)
        {
            double sync=Ease(local);
            Label(g,"SYNCHRO",353,244,72,White,true);Label(g,"HARMONICS",356,323,22,Cyan,true);
            Number(g,(sync*100).ToString("000.0",CultureInfo.InvariantCulture),826,249,108,Cyan);Label(g,"%",1154,319,35,Cyan);
            Label(g,"PILOT / CODEX",358,390,14,Muted);Label(g,"SYSTEM-01",1076,390,14,Muted);
            Wave(g,359,479,875,47,0,Cyan);Wave(g,359,479,875,47,(1-sync)*2.6,White);
            Line(g,358,554,1234,554,Dim,1);
            for(int i=0;i<58;i++)g.DrawRectangle(i<sync*58?Cyan:Dim,null,new Rect(359+i*15,581,10,27));
            Label(g,"NEURAL CONNECTION / PHASE ALIGNMENT",360,630,16,Cyan);
        }
        private void DrawMagi(DrawingContext g,double local)
        {
            Label(g,"MAGI",354,230,90,White,true);Label(g,"TRINITY AUTHORIZATION",604,260,26,Cyan,true);
            Computer(g,800,376,"BALTHASAR","02",local>0.28);
            Computer(g,540,530,"MELCHIOR","01",local>0.10);
            Computer(g,1060,530,"CASPER","03",local>0.47);
            Line(g,740,432,650,474,Cyan,1);Line(g,860,432,950,474,Cyan,1);Line(g,650,530,950,530,Cyan,1);
            double p=(seconds*0.7)%1;g.DrawEllipse(White,null,new Point(650+300*p,530),2.5,2.5);
            Center(g,local>0.6?"ALL SYSTEMS NOMINAL":"TRINITY / CONSENSUS CHECK",622,26,local>0.6?Mint:Cyan,true);
        }
        private void DrawReady(DrawingContext g)
        {
            Center(g,"CODEX",237,120,White,true);Center(g,"ONLINE",365,98,Cyan,true);
            for(int i=0;i<3;i++)
            {
                double x=402+i*272;g.DrawRectangle(Cyan,null,new Rect(x,498,252,31));Label(g,Names[i]+" / ACK",x+13,504,15,Black);
            }
            Hazard(g,353,562,894,17,Mint,seconds*11);
            Center(g,"LAUNCH AUTHORIZED",588,43,Mint,true);
            Center(g,confirmationReady?"MANUAL CONFIRMATION REQUIRED":"VOICE CHANNEL / FINAL CHECK",643,15,Cyan,false);
        }
        private void Computer(DrawingContext g,double x,double y,string name,string number,bool accepted)
        {
            var points=new[]{new Point(x-128,y-48),new Point(x-105,y-65),new Point(x+105,y-65),new Point(x+128,y-48),new Point(x+128,y+48),new Point(x+105,y+65),new Point(x-105,y+65),new Point(x-128,y+48)};
            Poly(g,points,accepted?Cyan:Steel,Cyan,2);Brush color=accepted?Black:Cyan;
            Label(g,number,x-110,y-48,14,color);CenterAt(g,name,x,y-28,29,color,true);CenterAt(g,accepted?"ACKNOWLEDGED":"VERIFYING",x,y+20,14,color,false);
        }
        private void Wave(DrawingContext g,double x,double y,double width,double height,double phase,Brush color)
        {
            var path=new StreamGeometry();using(var c=path.Open())for(int i=0;i<=150;i++)
            {double p=i/150.0,v=Math.Sin(p*34-seconds*2.3+phase)*Math.Sin(p*9+seconds*0.4+phase*0.3);var point=new Point(x+p*width,y+v*height);if(i==0)c.BeginFigure(point,false,false);else c.LineTo(point,true,false);}
            path.Freeze();g.PushOpacity(0.7);g.DrawGeometry(null,Pen(color,1.5),path);g.Pop();
        }
        private void Hazard(DrawingContext g,double x,double y,double width,double height,Brush color,double shift)
        {
            g.PushClip(new RectangleGeometry(new Rect(x,y,width,height)));double offset=shift%34;
            for(double p=x-40+offset;p<x+width+40;p+=34)Poly(g,new[]{new Point(p,y),new Point(p+14,y),new Point(p+14-height,y+height),new Point(p-height,y+height)},color,null,0);
            g.Pop();
        }
        private static Geometry Shape(Point[] points)
        {
            var path=new StreamGeometry();using(var c=path.Open()){c.BeginFigure(points[0],true,true);for(int i=1;i<points.Length;i++)c.LineTo(points[i],true,false);}path.Freeze();return path;
        }
        private void Poly(DrawingContext g,Point[] points,Brush fill,Brush stroke,double width){g.DrawGeometry(fill,stroke==null?null:Pen(stroke,width),Shape(points));}
        private static double Ease(double n){n=Math.Max(0,Math.Min(1,n));return n*n*(3-2*n);}
        private static Brush Paint(string hex){var b=(SolidColorBrush)new BrushConverter().ConvertFromString(hex);b.Freeze();return b;}
        private Pen Pen(Brush color,double width)
        {string key=color.ToString()+"/"+width.ToString(CultureInfo.InvariantCulture);Pen pen;if(!pens.TryGetValue(key,out pen)){pen=new Pen(color,width);pen.Freeze();pens[key]=pen;}return pen;}
        private void Line(DrawingContext g,double x1,double y1,double x2,double y2,Brush color,double width){g.DrawLine(Pen(color,width),new Point(x1,y1),new Point(x2,y2));}
        private void Label(DrawingContext g,string text,double x,double y,double size,Brush color,bool title=false){g.DrawText(Text(text,size,color,title),new Point(x,y));}
        private void Center(DrawingContext g,string text,double y,double size,Brush color,bool title){CenterAt(g,text,800,y,size,color,title);}
        private void CenterAt(DrawingContext g,string text,double x,double y,double size,Brush color,bool title){var t=Text(text,size,color,title);g.DrawText(t,new Point(x-t.Width/2,y));}
        private void Number(DrawingContext g,string value,double x,double y,double size,Brush color)
        {for(int i=0;i<value.Length;i++)Label(g,value[i].ToString(),x+i*size*0.6001,y,size,color);}
        private FormattedText Text(string text,double size,Brush color,bool title)
        {
            string key=text+"/"+size.ToString(CultureInfo.InvariantCulture)+"/"+color.ToString()+"/"+title;FormattedText t;
            if(!texts.TryGetValue(key,out t)){if(texts.Count>=192)texts.Clear();t=new FormattedText(text,CultureInfo.GetCultureInfo("en-US"),FlowDirection.LeftToRight,title?Display:Mono,size,color,1.0);texts[key]=t;}return t;
        }
    }
}
