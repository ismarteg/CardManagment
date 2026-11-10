namespace IdCard.Domain.Models;

public class CardDesign
{
    public int DesignVersion { get; set; } = 1;
    public CardSideDesign Front { get; set; } = new();
    public CardSideDesign Back { get; set; } = new();
}

public class CardSideDesign
{
    public string? BackgroundColor { get; set; } = "#FFFFFF";
    public string? BackgroundImage { get; set; }
    public List<DesignElement> Elements { get; set; } = new();
}

public class DesignElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "text"; // text, photo, image, qr, barcode, rect, line
    public string? Field { get; set; }
    public string? Text { get; set; }
    public string? Src { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 100;
    public double H { get; set; } = 30;
    public double FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "Arial";
    public string Color { get; set; } = "#000000";
    public string Align { get; set; } = "left";
    public int ZIndex { get; set; }
}
