using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace POS_System.Localization
{
    public class Loc : INotifyPropertyChanged
    {
        public static Loc I { get; } = new Loc();
        public Loc() { }

        public event PropertyChangedEventHandler? PropertyChanged;

        // يجيب النص من Strings.resx
        public string this[string key]
            => Resources.Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";

        //public CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;

        public void SetCulture(string cultureCode)
        {
            var culture = new CultureInfo(cultureCode);

            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            // ✅ اتجاه التطبيق
            var fd = culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            var lang = System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag);

            if (Application.Current != null)
            {
                foreach (Window w in Application.Current.Windows)
                {
                    w.FlowDirection = fd;
                    w.Language = lang;
                }
            }


            // ✅ احفظ اختيار اللغة

            AppState.SaveCulture(cultureCode);


            // ✅ تحديث كل الـ Bindings اللي بتستخدم Loc
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            //PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlowDirection)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));

        }
        public FlowDirection FlowDirection =>
            CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        public System.Windows.Markup.XmlLanguage Language =>
            System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);

    }
}
