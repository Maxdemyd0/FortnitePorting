using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FortnitePorting.Models;
using Material.Icons;

namespace FortnitePorting.Extensions;

public static class EnumExtensions
{
    private static readonly Geometry EmeraldVectorIcon = Geometry.Parse("M11,20 A7,7 0 0 1 9.8,6.1 C15.5,5 17,4.48 19,2 C20,4 21,6.18 21,10 C21,15.5 16.22,20 11,20 Z M2,21 C2,18 3.85,15.64 7.08,15 C9.5,14.52 12,13 13,12");
    private static readonly Geometry SunsetVectorIcon = Geometry.Parse("M12,10 L12,2 M4.93,10.93 L6.34,12.34 M2,18 L4,18 M20,18 L22,18 M19.07,10.93 L17.66,12.34 M22,22 L2,22 M16,6 L12,10 L8,6 M16,18 A4,4 0 0 0 8,18");

    extension(Enum value)
    {
        public string Description =>
            value.GetType()
                .GetField(value.ToString())?
                .GetCustomAttributes(typeof(DescriptionAttribute), false).SingleOrDefault() is not DescriptionAttribute attribute ? value.ToString() : attribute.Description;

        public bool IsDisabled =>
            value.GetType()
                .GetField(value.ToString())?.GetCustomAttributes(typeof(DisabledAttribute), false)
                .SingleOrDefault() is not null;
        
        public bool IsCosmetic =>
            value.GetType()
                .GetField(value.ToString())?.GetCustomAttributes(typeof(CosmeticAssetAttribute), false).SingleOrDefault() is not null;
        
        public MaterialIconKind? Icon =>
            value.GetType()
                .GetField(value.ToString())?
                .GetCustomAttributes(typeof(IconAttribute), false).SingleOrDefault() is not IconAttribute attribute ? null : attribute.Icon;

        public Geometry? VectorIcon => value switch
        {
            EThemeType.Emerald => EmeraldVectorIcon,
            EThemeType.Sunset => SunsetVectorIcon,
            _ => null
        };

        public EnumRecord ToEnumRecord()
        {
            return new EnumRecord(value.GetType(), value, value.Description, value.IsDisabled, value.Icon, value.VectorIcon);
        }
    }
    
    extension(EExportType exportType)
    {
        public bool IsAssetType =>
            exportType.GetType().GetField(exportType.ToString())?.GetCustomAttributes(typeof(NonAssetAttribute), false).SingleOrDefault() is null;
        
    }
}

public class EnumToItemsSource(Type type) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var values = Enum.GetValues(type).Cast<Enum>();
        return values.Select(value => value.ToEnumRecord()).ToList();
    }
}

public record EnumRecord(Type EnumType, Enum Value, string Description, bool IsDisabled = false, MaterialIconKind? Icon = null, Geometry? VectorIcon = null)
{
    public bool HasVectorIcon => VectorIcon is not null;

    public override string ToString()
    {
        return Description;
    }
}
