using System.ComponentModel;

namespace GestorFinanzas.Strings
{
    // Permite usar los textos traducidos directo en XAML, así:
    //
    //   xmlns:strings="clr-namespace:GestorFinanzas.Strings"
    //   ...
    //   <Label Text="{Binding [Ajustes_Titulo], Source={x:Static strings:Tr.Instance}}" />
    //
    // Cuando el usuario cambia el idioma, todos los controles que usan este
    // binding se actualizan solos (sin tener que recrear la pantalla).
    public class Tr : INotifyPropertyChanged
    {
        public static readonly Tr Instance = new Tr();

        public event PropertyChangedEventHandler? PropertyChanged;

        private Tr()
        {
            LocalizationManager.IdiomaCambiado += () =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }

        public string this[string clave] => AppStrings.Get(clave);
    }
}