using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
namespace FM24FaceAssistant;
public static class PortraitImageCache
{
    const int Capacity=96;
    static readonly Dictionary<string,(long Stamp,long Length,BitmapImage Image)> cache=new(StringComparer.OrdinalIgnoreCase);
    static readonly Queue<string> order=new();
    static readonly object gate=new();
    public static BitmapImage? Load(string? path)
    {
        if(string.IsNullOrWhiteSpace(path))return null;
        try{
            var file=new FileInfo(path);if(!file.Exists)return null;long stamp=file.LastWriteTimeUtc.Ticks,length=file.Length;
            lock(gate){
                if(cache.TryGetValue(path,out var value)&&value.Stamp==stamp&&value.Length==length)return value.Image;
                var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=250;image.UriSource=new Uri(file.FullName);image.CreateOptions=BitmapCreateOptions.IgnoreImageCache;image.EndInit();image.Freeze();
                if(!cache.ContainsKey(path))order.Enqueue(path);cache[path]=(stamp,length,image);
                while(cache.Count>Capacity&&order.Count>0)cache.Remove(order.Dequeue());return image;
            }
        }catch{return null;}
    }
}
public sealed class Thumbnails:IValueConverter
{
    public object? Convert(object value,Type target,object parameter,CultureInfo culture)=>PortraitImageCache.Load(value as string);
    public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
