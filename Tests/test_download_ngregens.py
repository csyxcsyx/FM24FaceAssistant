"""Offline download-pipeline checks; never downloads the real pack or opens FM."""
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
from pathlib import PurePosixPath
from PIL import Image, PngImagePlugin

script = Path(__file__).resolve().parents[1] / "download_ngregens.py"
spec = importlib.util.spec_from_file_location("ng_download", script)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

def png(metadata=False, color="red"):
    image=Image.new("RGBA",(2,2),color)
    stream=io.BytesIO()
    info=PngImagePlugin.PngInfo()
    if metadata:info.add_text("Description","same pixels, different metadata")
    image.save(stream,format="PNG",pnginfo=info)
    return stream.getvalue()

class PipelineTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix="FM24-pack-test-")
        self.root=Path(self.temp.name)
        self.changes=[]
        for key,value in dict(ASSETS=self.root/"assets",STATE=self.root/"state",ARCHIVE=self.root/"assets"/"pack.zip",PART=self.root/"assets"/"pack.part",STATUS=self.root/"assets"/"status.json").items():
            self.changes.append((key,getattr(mod,key)));setattr(mod,key,value)
        mod.ASSETS.mkdir()
    def tearDown(self):
        for key,value in self.changes:setattr(mod,key,value)
        self.temp.cleanup()
    def make_archive(self,unsafe=False):
        with zipfile.ZipFile(mod.ARCHIVE,"w",compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("NG_Regens/China/a.png",png())
            archive.writestr("NG_Regens/China/b.png",png(metadata=True))
            archive.writestr("NG_Regens/WF/China/female-player.png",png(color="green"))
            archive.writestr("NG_Regens/Staff/Female/Korea/c.png",png(color="blue"))
            if unsafe:archive.writestr("../escape.png",png())
    def test_extract_crc_classification_and_pixel_dedup(self):
        self.make_archive();mod.extract_and_index()
        report=json.loads((mod.ASSETS/"library-report.json").read_text(encoding="utf-8"))
        self.assertEqual(report["total_images"],3)
        self.assertEqual(report["unique_files"],3)
        self.assertEqual(report["unique_pixels"],2)
        rows=json.loads((mod.STATE/"catalog.json").read_text(encoding="utf-8"))
        self.assertEqual(rows[0]["VisualHash"],rows[1]["VisualHash"])
        staff=next(row for row in rows if row["Kind"]=="职员")
        self.assertEqual((staff["Gender"],staff["Group"]),("女","Korea"))
        self.assertFalse(any(row["Kind"]=="球员" and row["Gender"]=="女" for row in rows))
        self.assertTrue(all(Path(row["Path"]).exists() for row in rows))
        self.assertEqual(json.loads(mod.STATUS.read_text(encoding="utf-8"))["stage"],"complete")
    def test_path_traversal_rejected(self):
        self.make_archive(unsafe=True)
        with self.assertRaises(ValueError):mod.extract_and_index()
        self.assertFalse((mod.ASSETS/"escape.png").exists())
    def test_checkpoint_resume_skips_verified_files(self):
        with zipfile.ZipFile(mod.ARCHIVE,"w",compression=zipfile.ZIP_DEFLATED) as archive:
            for i in range(130):archive.writestr(f"NG_Regens/China/{i}.png",png())
        original_status = mod.status
        def interrupted(stage, **values):
            original_status(stage, **values)
            if stage == "indexing" and values.get("processed") == 128:
                raise RuntimeError("simulated interruption")
        with patch.object(mod,"status",interrupted),self.assertRaises(RuntimeError):mod.extract_and_index()
        self.assertFalse((mod.STATE/"catalog.json").exists())
        original_read = zipfile.ZipFile.read
        reads = []
        def counted(instance,entry,*args,**kwargs):
            reads.append(entry.filename)
            return original_read(instance,entry,*args,**kwargs)
        with patch.object(zipfile.ZipFile,"read",counted):mod.extract_and_index()
        self.assertEqual(len(reads),2)
        self.assertEqual(json.loads((mod.STATE/"catalog.json").read_text(encoding="utf-8"))[0]["Kind"],"球员")
    def test_checkpoint_revalidates_modified_image_and_archive(self):
        self.make_archive();mod.extract_and_index()
        rows=json.loads((mod.STATE/"catalog.json").read_text(encoding="utf-8"))
        target=Path(rows[0]["Path"])
        target.write_bytes(b"damaged")
        mod.extract_and_index()
        self.assertEqual(target.read_bytes(),png())
        with zipfile.ZipFile(mod.ARCHIVE,"a") as archive:
            archive.writestr("NG_Regens/China/new.png",png(color="yellow"))
        mod.extract_and_index()
        self.assertEqual(json.loads((mod.ASSETS/"library-report.json").read_text(encoding="utf-8"))["total_images"],4)
    def test_windows_extended_path_prefix_does_not_trigger_false_escape(self):
        root=self.root.resolve()
        normal=root/"NG_Regens"/"Iceland"/"face.png"
        with patch.object(Path,"resolve",return_value=Path("\\\\?\\"+str(normal))):
            self.assertEqual(mod.safe_target(root,PurePosixPath("NG_Regens/Iceland/face.png")),normal)
        outside=root.parent/"outside.png"
        with patch.object(Path,"resolve",return_value=Path("\\\\?\\"+str(outside))),self.assertRaises(ValueError):
            mod.safe_target(root,PurePosixPath("NG_Regens/Iceland/face.png"))
        self.assertEqual(mod.comparison_path(Path("\\\\?\\UNC\\server\\share\\face.png")),Path("\\\\server\\share\\face.png"))
    def test_status_write_retries_temporary_windows_sharing_lock(self):
        original=mod.os.replace
        calls=[]
        def flaky(source,target):
            calls.append(1)
            if len(calls)<3:raise PermissionError("temporary sharing lock")
            return original(source,target)
        with patch.object(mod.os,"replace",flaky):mod.status("downloading",downloaded=42,total=100)
        self.assertEqual(len(calls),3)
        self.assertEqual(json.loads(mod.STATUS.read_text(encoding="utf-8"))["downloaded"],42)
    def test_wf_is_female_player_directory(self):
        self.assertEqual(mod.classify(PurePosixPath("NG_Regens/WF/China/a.png")),("China","球员","女"))
    def test_corrupt_entry_crc_rejected_before_catalog_commit(self):
        name="NG_Regens/China/corrupt.png"
        with zipfile.ZipFile(mod.ARCHIVE,"w",compression=zipfile.ZIP_STORED) as archive:
            archive.writestr(name,png())
        data=bytearray(mod.ARCHIVE.read_bytes())
        data[30+len(name.encode("utf-8"))+12]^=1
        mod.ARCHIVE.write_bytes(data)
        with self.assertRaises(zipfile.BadZipFile):mod.extract_and_index()
        self.assertFalse((mod.STATE/"catalog.json").exists())
    def test_retained_prefix_verified_then_appended(self):
        data=b"PK\x03\x04"+b"abc"*1000
        mod.PART.write_bytes(data[:100])
        class Fake:
            def __init__(self,*args,**kwargs):
                assert args[0][1] == "--disable"
                assert "--noproxy" in args[0] and "--proxy" in args[0]
                assert kwargs["env"]["NO_PROXY"] == "*"
                assert not any(k.lower() in {"http_proxy","https_proxy","all_proxy"} for k in kwargs["env"])
                (mod.ASSETS/"download.headers").write_text(f"HTTP/1.1 200 OK\nContent-Length: {len(data)}\n")
                self.stdout=io.BytesIO(data)
            def wait(self):return 0
            def poll(self):return 0
        with patch.object(mod,"EXPECTED",len(data)),patch.object(mod.subprocess,"Popen",Fake):mod.download()
        self.assertEqual(mod.ARCHIVE.read_bytes(),data)
        self.assertFalse(mod.PART.exists())
    def test_changed_source_does_not_overwrite_prefix(self):
        data=b"PK\x03\x04"+b"abc"*100
        original=b"different source prefix"
        mod.PART.write_bytes(original)
        class Fake:
            def __init__(self,*args,**kwargs):
                (mod.ASSETS/"download.headers").write_text(f"Content-Length: {len(data)}\n")
                self.stdout=io.BytesIO(data)
            def wait(self):return 0
            def poll(self):return 0
        with patch.object(mod,"EXPECTED",len(data)),patch.object(mod.subprocess,"Popen",Fake),self.assertRaises(ValueError):mod.download()
        self.assertEqual(mod.PART.read_bytes(),original)

if __name__=="__main__":unittest.main(verbosity=2)
