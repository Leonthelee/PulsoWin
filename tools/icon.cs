#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property OutputType=Exe

// Genera el ícono de PulsoWin (mando retro neón) en varios tamaños y los empaqueta en un .ico.
// Uso (desde la raíz): dotnet run tools/icon.cs -- src/PulsoWin.App/Assets/pulsowin.ico icono-256.png
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var icoPath = args[0];
var previewPath = args[1];

var pngs = new List<byte[]>();
var thread = new Thread(() =>
{
    foreach (var s in sizes) pngs.Add(Render(s));
    File.WriteAllBytes(previewPath, pngs[^1]);
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

using (var fs = File.Create(icoPath))
using (var w = new BinaryWriter(fs))
{
    w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);   // ICONDIR
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)                                  // ICONDIRENTRY
    {
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)0); w.Write((byte)0);
        w.Write((short)1); w.Write((short)32);
        w.Write(pngs[i].Length); w.Write(offset);
        offset += pngs[i].Length;
    }
    foreach (var png in pngs) w.Write(png);                                  // PNG por tamaño (válido desde Vista)
}
Console.WriteLine($"{icoPath}: {sizes.Length} tamaños ({string.Join(", ", sizes)}) · {new FileInfo(icoPath).Length} bytes");

static byte[] Render(int s)
{
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    double u = s / 100.0;                       // trabajo en una cuadrícula de 100 × 100
    bool tiny = s < 32;
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        // Fondo: cuadrado redondeado oscuro con borde magenta suave
        var bg = new LinearGradientBrush(C("#241A4A"), C("#0E0B1A"), 90);
        dc.DrawRoundedRectangle(bg, new Pen(new SolidColorBrush(C("#FF3EA5")), Math.Max(1, 2.5 * u)),
            new Rect(1.5 * u, 1.5 * u, 97 * u, 97 * u), 22 * u, 22 * u);

        // Cuerpo del mando
        var body = new LinearGradientBrush(C("#6A56D4"), C("#2E2470"), 90);
        var outline = new Pen(new SolidColorBrush(C("#2DE2E6")), Math.Max(1, (tiny ? 7 : 5) * u));
        dc.DrawRoundedRectangle(body, outline, new Rect(10 * u, 28 * u, 80 * u, 44 * u), 14 * u, 14 * u);

        // Cruceta (clara en tamaños pequeños para que se lea sobre el violeta)
        var cross = new SolidColorBrush(tiny ? C("#EDE9FF") : C("#0B0918"));
        double cx = 30, cy = 50, arm = tiny ? 13 : 12, thick = tiny ? 7 : 6.5;
        dc.DrawRoundedRectangle(cross, null, new Rect((cx - thick / 2) * u, (cy - arm) * u, thick * u, 2 * arm * u), 1.5 * u, 1.5 * u);
        dc.DrawRoundedRectangle(cross, null, new Rect((cx - arm) * u, (cy - thick / 2) * u, 2 * arm * u, thick * u), 1.5 * u, 1.5 * u);

        // Botones A (magenta) y B (cian)
        double r = tiny ? 8 : 7;
        dc.DrawEllipse(new SolidColorBrush(C("#FF3EA5")), null, new Point(74 * u, 43 * u), r * u, r * u);
        dc.DrawEllipse(new SolidColorBrush(C("#2DE2E6")), null, new Point(61 * u, 57 * u), r * u, r * u);

        // Select / Start (solo en tamaños grandes)
        if (s >= 48)
        {
            var pill = new SolidColorBrush(C("#9B86FF"));
            dc.DrawRoundedRectangle(pill, null, new Rect(43 * u, 37 * u, 9 * u, 3.5 * u), 1.75 * u, 1.75 * u);
            dc.DrawRoundedRectangle(pill, null, new Rect(43 * u, 44 * u, 9 * u, 3.5 * u), 1.75 * u, 1.75 * u);
        }
    }

    var bmp = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
    bmp.Render(visual);
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using var ms = new MemoryStream();
    enc.Save(ms);
    return ms.ToArray();
}
