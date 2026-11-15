using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Etiquetador.App;

/// <summary>Avisos y preguntas sencillos (Avalonia no trae cuadros de mensaje).</summary>
public static class Dialogos
{
    public static IBrush Fondo { get; } = new SolidColorBrush(Color.Parse("#dce9f7"));
    public static IBrush Gris { get; } = new SolidColorBrush(Color.Parse("#5d6874"));

    private static Window Crear(string titulo, string texto, params (string Texto, object? Resultado)[] botones)
    {
        var ventana = new Window
        {
            Title = titulo,
            Background = Fondo,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 340,
            MaxWidth = 720,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var fila = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var (t, r) in botones)
        {
            var b = new Button { Content = t, MinWidth = 80 };
            var resultado = r;
            b.Click += (_, _) => ventana.Close(resultado);
            fila.Children.Add(b);
        }
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, MaxWidth = 660 });
        panel.Children.Add(fila);
        ventana.Content = panel;
        return ventana;
    }

    public static async Task Mensaje(Window dueno, string titulo, string texto)
    {
        var v = Crear(titulo, texto, ("Aceptar", true));
        await v.ShowDialog<object?>(dueno);
    }

    public static async Task<bool> Preguntar(Window dueno, string titulo, string texto)
    {
        var v = Crear(titulo, texto, ("Sí", true), ("No", false));
        var r = await v.ShowDialog<object?>(dueno);
        return r is true;
    }
}
