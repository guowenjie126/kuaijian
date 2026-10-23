namespace KuaiJian.WinUI;

public static class ImagePreviewScalePolicy
{
    public static (double Width, double Height) FitToViewport(
        int pixelWidth,
        int pixelHeight,
        double viewportWidth,
        double viewportHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0
            || !double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight)
            || viewportWidth <= 0 || viewportHeight <= 0)
            return (0, 0);

        var scale = Math.Min(1, Math.Min(viewportWidth / pixelWidth, viewportHeight / pixelHeight));
        return (pixelWidth * scale, pixelHeight * scale);
    }
}
