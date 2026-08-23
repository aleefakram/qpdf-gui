namespace QPdfDecryptor.Operations;

public static class QpdfLocator
{
    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "Native", "qpdf.exe");
}
