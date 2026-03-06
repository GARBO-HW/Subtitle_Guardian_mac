using SkiaSharp;
using System.IO;

class Program
{
    static void Main(string[] args)
    {
        string outputDir = "AppIcon.iconset";
        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        Directory.CreateDirectory(outputDir);

        int[] sizes = { 16, 32, 64, 128, 256, 512, 1024 };

        foreach (var size in sizes)
        {
            using var bitmap = new SKBitmap(size, size);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            DrawOmamori(canvas, size);

            // Save as icon_{size}x{size}.png
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            
            string filename = $"icon_{size}x{size}.png";
            string path = Path.Combine(outputDir, filename);
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);
            
            // Also need @2x versions for some sizes
            if (size <= 512)
            {
                 // Usually for 16x16, we need icon_16x16.png and icon_16x16@2x.png (which is 32x32)
                 // But simply generating standard sizes is often enough if we name them correctly for iconutil.
                 // iconutil expects:
                 // icon_16x16.png
                 // icon_16x16@2x.png (32x32)
                 // icon_32x32.png
                 // icon_32x32@2x.png (64x64)
                 // ...
            }
        }
        
        // Proper iconset generation
        GenerateIconSet(outputDir);
        
        // Generate DMG Background
        GenerateDmgBackground();
    }

    static void GenerateDmgBackground()
    {
        int width = 600;
        int height = 400;
        
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        
        // Background color (Dark Gray like Finder Dark Mode or just a nice neutral)
        canvas.Clear(SKColor.Parse("#1E1E1E"));
        
        // Draw Arrow (Smaller)
        var arrowPaint = new SKPaint 
        { 
            Color = SKColor.Parse("#F1FAEE"), // White/Off-white
            Style = SKPaintStyle.Stroke, 
            StrokeWidth = 6, // Thinner stroke
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        
        // Arrow path - shorter
        // Center is (300, 200)
        // Original: 240->360 (120px). New: 270->330 (60px)
        var path = new SKPath();
        path.MoveTo(270, 200);
        path.LineTo(330, 200);
        
        // Arrow head - smaller
        path.MoveTo(310, 180);
        path.LineTo(330, 200);
        path.LineTo(310, 220);
        
        canvas.DrawPath(path, arrowPaint);
        
        // Optional: Text "Drag to Applications"
        var textPaint = new SKPaint 
        { 
            Color = SKColor.Parse("#F1FAEE"), 
            IsAntialias = true, 
            TextAlign = SKTextAlign.Center,
            TextSize = 14, // Smaller text
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };
        
        // Draw text below arrow
        // Note: SKCanvas.DrawText(string, float, float, SKPaint) is obsolete but still works in this version.
        // We can use the newer API or just suppress warning.
        // For simplicity in this quick tool, we use the simple one.
        canvas.DrawText("Drag to Applications", 300, 240, textPaint);
        
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        
        string filename = "dmg_background.png";
        using var stream = File.OpenWrite(filename);
        data.SaveTo(stream);
        Console.WriteLine($"Generated {filename}");
    }

    static void GenerateIconSet(string outputDir)
    {
        // icon_16x16.png
        DrawAndSave(outputDir, "icon_16x16.png", 16);
        // icon_16x16@2x.png == 32x32
        DrawAndSave(outputDir, "icon_16x16@2x.png", 32);
        
        // icon_32x32.png
        DrawAndSave(outputDir, "icon_32x32.png", 32);
        // icon_32x32@2x.png == 64x64
        DrawAndSave(outputDir, "icon_32x32@2x.png", 64);
        
        // icon_128x128.png
        DrawAndSave(outputDir, "icon_128x128.png", 128);
        // icon_128x128@2x.png == 256x256
        DrawAndSave(outputDir, "icon_128x128@2x.png", 256);
        
        // icon_256x256.png
        DrawAndSave(outputDir, "icon_256x256.png", 256);
        // icon_256x256@2x.png == 512x512
        DrawAndSave(outputDir, "icon_256x256@2x.png", 512);
        
        // icon_512x512.png
        DrawAndSave(outputDir, "icon_512x512.png", 512);
        // icon_512x512@2x.png == 1024x1024
        DrawAndSave(outputDir, "icon_512x512@2x.png", 1024);
    }

    static void DrawAndSave(string dir, string filename, int size)
    {
        using var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        DrawOmamori(canvas, size);
        
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(Path.Combine(dir, filename));
        data.SaveTo(stream);
        Console.WriteLine($"Generated {filename}");
    }

    static void DrawOmamori(SKCanvas canvas, int size)
    {
        // Scale everything to size
        float scale = size / 512f;
        canvas.Scale(scale);

        // Palette
        var bgPink = SKColor.Parse("#F4C2C2"); // Soft Pink
        var borderPink = SKColor.Parse("#CD979E"); // Darker Pink border
        var knotPink = SKColor.Parse("#E5989B"); // Knot color
        var knotDark = SKColor.Parse("#B5838D"); // Knot details
        var yellow = SKColor.Parse("#F9DC5C"); // Yellow accent
        var darkAccent = SKColor.Parse("#FF6B6B"); // Red/Dark Pink accent
        var white = SKColor.Parse("#FFFFFF");
        var textGray = SKColor.Parse("#6D6875"); // Grayish purple for text

        // Main Body Shape
        // Rounded Rectangle
        var bodyRect = new SKRect(100, 100, 412, 480);
        var cornerRadius = 60f;
        var bodyPath = new SKPath();
        bodyPath.AddRoundRect(bodyRect, cornerRadius, cornerRadius);

        // Draw Body Background
        var bgPaint = new SKPaint { Color = bgPink, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawPath(bodyPath, bgPaint);

        // Draw Bottom Right Pattern
        // We need to clip to the body path so pattern doesn't spill out
        canvas.Save();
        canvas.ClipPath(bodyPath);

        // Yellow Rectangle part (vertical strip)
        var patternY = new SKPaint { Color = yellow, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRect(300, 350, 60, 130, patternY);

        // Dark Pink Quarter Circle part
        var patternR = new SKPaint { Color = darkAccent, Style = SKPaintStyle.Fill, IsAntialias = true };
        var arcPath = new SKPath();
        arcPath.MoveTo(360, 480);
        arcPath.LineTo(412, 480); // Bottom right corner
        arcPath.LineTo(412, 380);
        // Curve back to (360, 480) ?? Or just a quarter circle.
        // Let's draw a circle at corner (412, 480) with radius ~100
        canvas.DrawCircle(412, 480, 80, patternR);
        
        canvas.Restore();

        // Draw Body Border
        var borderPaint = new SKPaint 
        { 
            Color = borderPink, 
            Style = SKPaintStyle.Stroke, 
            StrokeWidth = 16, 
            IsAntialias = true 
        };
        canvas.DrawPath(bodyPath, borderPaint);

        // Center Tag (White)
        var tagRect = new SKRect(160, 200, 352, 380);
        var tagPath = new SKPath();
        tagPath.AddRoundRect(tagRect, 40, 40);
        
        var tagFill = new SKPaint { Color = white, Style = SKPaintStyle.Fill, IsAntialias = true };
        // Slight shadow/border for tag
        var tagBorder = new SKPaint { Color = borderPink.WithAlpha(100), Style = SKPaintStyle.Stroke, StrokeWidth = 4, IsAntialias = true };
        
        canvas.DrawPath(tagPath, tagFill);
        canvas.DrawPath(tagPath, tagBorder);

        // Text "御守"
        var fontPaint = new SKPaint 
        { 
            Color = textGray, 
            IsAntialias = true, 
            TextAlign = SKTextAlign.Center,
            TextSize = 80,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) 
        };

        // Draw "御"
        canvas.DrawText("御", 256, 275, fontPaint);
        // Draw "守"
        canvas.DrawText("守", 256, 355, fontPaint);

        // Top Knot / Bow
        // Two large loops
        var knotPaint = new SKPaint { Color = knotPink, Style = SKPaintStyle.Fill, IsAntialias = true };
        var knotStroke = new SKPaint { Color = borderPink, Style = SKPaintStyle.Stroke, StrokeWidth = 8, IsAntialias = true };

        // Left Loop
        canvas.DrawOval(new SKRect(130, 40, 250, 140), knotPaint);
        canvas.DrawOval(new SKRect(130, 40, 250, 140), knotStroke);
        
        // Right Loop
        canvas.DrawOval(new SKRect(262, 40, 382, 140), knotPaint);
        canvas.DrawOval(new SKRect(262, 40, 382, 140), knotStroke);

        // Center Knot Circle
        var centerKnot = new SKPaint { Color = knotDark, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(256, 90, 30, centerKnot);
        canvas.DrawCircle(256, 90, 30, knotStroke);

        // String hanging down
        var stringPaint = new SKPaint { Color = knotDark, Style = SKPaintStyle.Stroke, StrokeWidth = 10, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        canvas.DrawLine(256, 120, 256, 180, stringPaint);
    }
}
