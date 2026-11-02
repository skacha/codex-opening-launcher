using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

// 只生成测试用的几何图案，不读取用户素材。
public static class TestFixtures
{
    static BinaryWriter writer;
    static long Begin(string id) { writer.Write(Encoding.ASCII.GetBytes(id)); long at = writer.BaseStream.Position; writer.Write(0); return at; }
    static void End(long at) { long end = writer.BaseStream.Position; writer.BaseStream.Position = at; writer.Write((int)(end - at - 4)); writer.BaseStream.Position = end; }
    static void Id(string value) { writer.Write(Encoding.ASCII.GetBytes(value)); }
    public static void Main(string[] args)
    {
        Directory.CreateDirectory(args[0]);
        using (var image = new Bitmap(640, 360))
        {
            using (var g = Graphics.FromImage(image))
            {
                g.Clear(Color.FromArgb(12, 28, 32)); g.FillEllipse(Brushes.MediumAquamarine, 220, 80, 200, 200);
                using (var font = new Font("Consolas", 38)) g.DrawString(">_", font, Brushes.Black, 275, 143);
            }
            image.Save(Path.Combine(args[0], "中文 素材 #1.png"), ImageFormat.Png);
        }
        using (writer = new BinaryWriter(File.Create(Path.Combine(args[0], "motion.avi"))))
        {
            long riff = Begin("RIFF"); Id("AVI ");
            long hdrl = Begin("LIST"); Id("hdrl");
            long avih = Begin("avih");
            foreach (int n in new[] { 100000, 1728000, 0, 0, 20, 0, 1, 172800, 320, 180, 0, 0, 0, 0 }) writer.Write(n);
            End(avih);
            long strl = Begin("LIST"); Id("strl"); long strh = Begin("strh");
            Id("vids"); Id("DIB "); writer.Write(0); writer.Write((short)0); writer.Write((short)0);
            foreach (int n in new[] { 0, 1, 10, 0, 20, 172800, -1, 0 }) writer.Write(n);
            writer.Write((short)0); writer.Write((short)0); writer.Write((short)320); writer.Write((short)180); End(strh);
            long strf = Begin("strf"); writer.Write(40); writer.Write(320); writer.Write(180); writer.Write((short)1); writer.Write((short)24);
            foreach (int n in new[] { 0, 172800, 0, 0, 0, 0 }) writer.Write(n);
            End(strf); End(strl); End(hdrl);
            long movi = Begin("LIST"); Id("movi");
            for (int frame = 0; frame < 20; frame++)
            {
                long chunk = Begin("00db");
                for (int y = 0; y < 180; y++) for (int x = 0; x < 320; x++)
                { writer.Write((byte)(35 + frame * 5)); writer.Write((byte)(80 + x / 3)); writer.Write((byte)(20 + y / 2)); }
                End(chunk);
            }
            End(movi); End(riff);
        }
    }
}
