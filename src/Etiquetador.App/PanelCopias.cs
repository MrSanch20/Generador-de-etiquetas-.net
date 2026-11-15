using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Etiquetador.App;

/// <summary>Una casilla «Copias» por esquema, con su botón ✕ para quitarlo. Se conserva lo que ya se había puesto.</summary>
public sealed class PanelCopias : WrapPanel
{
    private readonly Sesion _s;
    private readonly Action _alCambiar;
    private readonly Func<string, Task> _quitar;

    public PanelCopias(Sesion sesion, Action alCambiar, Func<string, Task> quitar)
    {
        _s = sesion; _alCambiar = alCambiar; _quitar = quitar;
        Margin = new Thickness(0, 0, 0, 6);
    }

    public void Refrescar()
    {
        Children.Clear();
        var esquemas = _s.Esquemas();
        if (esquemas.Count == 0) return;
        Children.Add(new TextBlock { Text = "Copias:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        foreach (var esq in esquemas)
        {
            var e = esq;
            var caja = new NumericUpDown
            {
                Minimum = 1, Maximum = 99, Increment = 1, FormatString = "0", Width = 110,
                Value = _s.Copias.GetValueOrDefault(e, 1),
            };
            caja.ValueChanged += (_, args) =>
            {
                _s.Copias[e] = (int)Math.Max(1, args.NewValue ?? 1);
                _alCambiar();
            };
            var quitar = new Button { Content = "✕", Margin = new Thickness(4, 0, 0, 0) };
            quitar.Click += async (_, _) => await _quitar(e);
            var celda = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 1, 18, 1) };
            celda.Children.Add(new TextBlock { Text = e, VerticalAlignment = VerticalAlignment.Center });
            celda.Children.Add(caja); celda.Children.Add(quitar);
            Children.Add(celda);
        }
        Children.Add(new TextBlock { Text = "(✕ quita el esquema)", Foreground = Dialogos.Gris, VerticalAlignment = VerticalAlignment.Center });
    }
}
