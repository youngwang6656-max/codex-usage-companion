using CodexUsageCompanion.Windows;
using Xunit;

namespace CodexUsageCompanion.Tests;

public sealed class ComposerAnchorDetectorTests
{
    [Fact]
    public void DetectsBrightCircularSendButtonBeforeScrollbar()
    {
        const int width = 260;
        const int height = 220;
        byte[] pixels = CreateSolidBgra(width, height, 24);
        DrawFilledCircle(pixels, width, height, 150, 164, 24, 238);
        DrawRectangle(pixels, width, height, 242, 70, 249, 199, 78);
        var capture = new ComposerCapture(
            pixels,
            width,
            height,
            width * 4,
            new PixelRect(940, 680, 1200, 900),
            96);

        bool found = ComposerAnchorDetector.TryDetectLandmarks(
            capture,
            out ComposerLandmarks landmarks);

        Assert.True(found);
        Assert.InRange(landmarks.SendCenterX, 1087, 1093);
        Assert.InRange(landmarks.SendCenterY, 841, 847);
        Assert.InRange(landmarks.ScrollbarLeft, 1181, 1184);
        Assert.True(landmarks.Confidence >= 0.65);
    }

    [Fact]
    public void FlatCaptureDoesNotProduceFalseAnchor()
    {
        const int width = 260;
        const int height = 220;
        var capture = new ComposerCapture(
            CreateSolidBgra(width, height, 24),
            width,
            height,
            width * 4,
            new PixelRect(940, 680, 1200, 900),
            96);

        Assert.False(ComposerAnchorDetector.TryDetectLandmarks(capture, out _));
    }

    [Fact]
    public void DetectsOutlinedDisabledSendButton()
    {
        const int width = 260;
        const int height = 220;
        byte[] pixels = CreateSolidBgra(width, height, 24);
        DrawCircleOutline(pixels, width, height, 150, 164, 24, 2, 150);
        DrawRectangle(pixels, width, height, 242, 70, 249, 199, 78);
        var capture = new ComposerCapture(
            pixels,
            width,
            height,
            width * 4,
            new PixelRect(940, 680, 1200, 900),
            96);

        Assert.True(ComposerAnchorDetector.TryDetectLandmarks(
            capture,
            out ComposerLandmarks landmarks));
        Assert.True(landmarks.Confidence >= 0.65);
    }

    [Fact]
    public void ChoosesRightmostSendButtonOverStrongerCircularControl()
    {
        const int width = 260;
        const int height = 220;
        byte[] pixels = CreateSolidBgra(width, height, 24);
        DrawFilledCircle(pixels, width, height, 110, 120, 24, 245);
        DrawFilledCircle(pixels, width, height, 170, 164, 24, 155);
        DrawRectangle(pixels, width, height, 242, 70, 249, 199, 78);
        var capture = new ComposerCapture(
            pixels,
            width,
            height,
            width * 4,
            new PixelRect(940, 680, 1200, 900),
            96);

        Assert.True(ComposerAnchorDetector.TryDetectLandmarks(
            capture,
            out ComposerLandmarks landmarks));
        Assert.InRange(landmarks.SendCenterX, 1106, 1114);
        Assert.InRange(landmarks.SendCenterY, 840, 848);
    }

    private static byte[] CreateSolidBgra(int width, int height, byte luminance)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = luminance;
            pixels[index + 1] = luminance;
            pixels[index + 2] = luminance;
            pixels[index + 3] = 255;
        }

        return pixels;
    }

    private static void DrawFilledCircle(
        byte[] pixels,
        int width,
        int height,
        int centerX,
        int centerY,
        int radius,
        byte luminance)
    {
        for (int y = Math.Max(0, centerY - radius); y <= Math.Min(height - 1, centerY + radius); y++)
        {
            for (int x = Math.Max(0, centerX - radius); x <= Math.Min(width - 1, centerX + radius); x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                if ((dx * dx) + (dy * dy) <= radius * radius)
                {
                    SetPixel(pixels, width, x, y, luminance);
                }
            }
        }
    }

    private static void DrawRectangle(
        byte[] pixels,
        int width,
        int height,
        int left,
        int top,
        int right,
        int bottom,
        byte luminance)
    {
        for (int y = Math.Max(0, top); y <= Math.Min(height - 1, bottom); y++)
        {
            for (int x = Math.Max(0, left); x <= Math.Min(width - 1, right); x++)
            {
                SetPixel(pixels, width, x, y, luminance);
            }
        }
    }

    private static void DrawCircleOutline(
        byte[] pixels,
        int width,
        int height,
        int centerX,
        int centerY,
        int radius,
        int thickness,
        byte luminance)
    {
        int innerSquared = (radius - thickness) * (radius - thickness);
        int outerSquared = (radius + thickness) * (radius + thickness);
        for (int y = Math.Max(0, centerY - radius - thickness);
             y <= Math.Min(height - 1, centerY + radius + thickness);
             y++)
        {
            for (int x = Math.Max(0, centerX - radius - thickness);
                 x <= Math.Min(width - 1, centerX + radius + thickness);
                 x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                int distanceSquared = (dx * dx) + (dy * dy);
                if (distanceSquared >= innerSquared && distanceSquared <= outerSquared)
                {
                    SetPixel(pixels, width, x, y, luminance);
                }
            }
        }
    }

    private static void SetPixel(
        byte[] pixels,
        int width,
        int x,
        int y,
        byte luminance)
    {
        int index = ((y * width) + x) * 4;
        pixels[index] = luminance;
        pixels[index + 1] = luminance;
        pixels[index + 2] = luminance;
    }
}
