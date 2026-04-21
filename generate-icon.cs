using System.Drawing;
using System.Drawing.Drawing2D;

int size = 64;
using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
{
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(Color.Transparent);

    var pen = new Pen(Color.FromArgb(59, 130, 246), size / 20f)
    {
        StartCap = System.Drawing.Drawing2D.LineCap.Round,
        EndCap = System.Drawing.Drawing2D.LineCap.Round
    };

    float margin = size * 0.08f;
    float r = (size - margin * 2) / 2f;
    float cx = size / 2f;
    float cy = size / 2f;

    // Outer circle
    g.DrawEllipse(pen, margin, margin, r * 2, r * 2);

    // Horizontal ellipse
    g.DrawEllipse(pen, margin, cy - r * 0.35f, r * 2, r * 0.7f);

    // Vertical line
    g.DrawLine(pen, cx, margin, cx, size - margin);

    // Curved side lines (simplified as ellipses)
    g.DrawEllipse(pen, cx - r * 0.6f, margin, r * 1.2f, r * 2);
    g.DrawEllipse(pen, cx - r * 0.3f, margin, r * 0.6f, r * 2);

    pen.Dispose();
}

bmp.Save(@"C:\Users\markl\Desktop\ai\Quickstart\Quickstart\Resources\web-url.png");
Console.WriteLine("Icon generated.");
