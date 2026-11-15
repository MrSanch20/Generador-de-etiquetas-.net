namespace Etiquetador.Core.Modelos;

/// <summary>Dirección de un texto (con la Y hacia abajo, como en pantalla), redondeada a 1 decimal.</summary>
public readonly record struct Direccion(double X, double Y)
{
    public static readonly Direccion Horizontal = new(1.0, 0.0);
    /// <summary>Girado 90°, se lee de abajo arriba.</summary>
    public static readonly Direccion Vertical = new(0.0, -1.0);

    public static Direccion Redondeada(double x, double y) =>
        new(Math.Round(x, 1, MidpointRounding.ToEven) + 0.0, Math.Round(y, 1, MidpointRounding.ToEven) + 0.0);
}

/// <summary>Color (r, g, b) de 0 a 1.</summary>
public readonly record struct Rgb(double R, double G, double B)
{
    public Rgb Redondeado() => new(Math.Round(R, 2), Math.Round(G, 2), Math.Round(B, 2));
}

/// <summary>Línea recta del dibujo vectorial de una página.</summary>
public readonly record struct Trazo(double X0, double Y0, double X1, double Y1, Rgb Color);

/// <summary>Flecha de referencia a otra hoja (triángulo relleno).</summary>
public readonly record struct Flecha(double X, double Y, Rgb Color);

/// <summary>Borna dibujada como circulito.</summary>
public readonly record struct Circulo(double X0, double Y0, double X1, double Y1);
