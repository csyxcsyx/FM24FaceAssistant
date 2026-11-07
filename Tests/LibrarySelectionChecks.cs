using FM24FaceAssistant;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class LibrarySelectionChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var skin=Path.Combine(root,"wrong skin 文件夹");Directory.CreateDirectory(Path.Combine(skin,"graphics","icons"));
        var icon=Path.Combine(skin,"graphics","icons","shirt.png");File.WriteAllBytes(icon,LibraryDownloadChecks.Png());
        bool rejected=false;try{Catalog.Faces(skin,default);}catch(IOException ex){rejected=ex.Message.Contains("下载 / 接续图库");}
        check(rejected,"皮肤图片目录不能索引为 NG_Regens 头像图库，并提示下载入口");
        var state=Path.Combine(root,"selection-cache");var original=Files.State;
        try
        {
            Files.State=state;Files.Save(Path.Combine(state,"catalog.json"),Enumerable.Range(0,695).Select(_=>new Face(icon,"icons","球员","男","fixture")).ToList());
            check(FaceLibraryStore.Load(default,_=>{},skin).Count==0,"旧版误索引的 695 张皮肤图片不再显示为可用头像");
            check(FaceLibraryStore.Load(default,_=>{},skin).Count==0,"SQLite 热缓存同样排除旧版皮肤图标");
            var library=Path.Combine(root,"分类素材");Directory.CreateDirectory(Path.Combine(library,"China"));Directory.CreateDirectory(Path.Combine(library,"Staff","Female","Korea"));
            File.WriteAllBytes(Path.Combine(library,"China","face.png"),LibraryDownloadChecks.Png());File.WriteAllBytes(Path.Combine(library,"Staff","Female","Korea","face.png"),LibraryDownloadChecks.Png(false,40));
            foreach(var sex in new[]{"male","female"}){var folder=Path.Combine(library,"Staff","China",sex);Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"face.png"),LibraryDownloadChecks.Png());}
            var rows=Catalog.Faces(library,default);check(rows.Count==4&&rows.Any(f=>f.Kind=="职员"&&f.Gender=="女"),"已解压的有效地区头像目录仍可手动索引");
            check(rows.Count(f=>f.Kind=="职员"&&f.Group=="China"&&f.Gender is "男" or "女")==2,"官方 Staff/地区/性别 目录可手动索引，不把性别文件夹误当作地区");
            Files.Save(Path.Combine(state,"catalog.json"),rows);check(FaceLibraryStore.Load(default,_=>{},library).Count==4,"正确图库替换误选目录后可立即加载");
            var oldDownload=Path.Combine(root,"旧下载盘","NG_Regens_Library");Directory.CreateDirectory(oldDownload);
            check(LibraryPaths.DownloadDirectory(root,state,null,oldDownload)==Path.GetDirectoryName(oldDownload),"程序移动后从已保存的 NG_Regens 图库位置找到旧下载数据");
            check(LibraryPaths.DownloadDirectory(root,state,null,oldDownload+Path.DirectorySeparatorChar)==Path.GetDirectoryName(oldDownload),"旧图库路径带末尾分隔符时仍定位到正确下载目录");
            check(LibraryPaths.DownloadDirectory(root,state,library,oldDownload)==library,"新选择的下载目录优先于旧图库位置");
            var custom=Path.Combine(root,"作者自定义图库");Directory.CreateDirectory(custom);File.WriteAllBytes(Path.Combine(custom,"face.png"),LibraryDownloadChecks.Png());Files.Save(Path.Combine(custom,"faces.json"),new[]{new Face("face.png","作者分类","职员","男","")});
            var customRows=Catalog.Faces(custom,default);Files.Save(Path.Combine(state,"catalog.json"),customRows);check(FaceLibraryStore.Load(default,_=>{},custom).Single().Group=="作者分类","显式 faces.json 作者清单仍支持自定义分类");
        }
        finally{Files.State=original;}
    }
}
