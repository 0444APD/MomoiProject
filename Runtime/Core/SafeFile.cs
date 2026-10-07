using System;
using System.IO;
using System.Text;

namespace GBible
{
    // 저장 파일 쓰기·읽기(프로필·판·슬롯·설정·서버 계정). 제자리에 바로 덮어쓰면 쓰는 도중 꺼지거나 디스크가 차면
    // 파일이 0바이트·반쪽이 되고 옛 내용도 사라진다. 그래서:
    //   · 쓰기: 임시 파일(.tmp)에 끝까지 쓰고 디스크에 내린 뒤 한 번에 바꿔 넣는다. 바로 전 파일은 .bak으로 하나 남긴다.
    //   · 읽기: 파일이 비었으면(공백뿐) 깨진 것으로 본다 — 부르는 쪽이 .bak으로 되돌릴 수 있게 Backup 경로를 준다.
    public static class SafeFile
    {
        public static string Backup(string path) => path + ".bak";
        static string Temp(string path) => path + ".tmp";

        public static void WriteAllText(string path, string text)
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            var temp = Temp(path);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(text ?? "");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }
            try
            {
                File.Replace(temp, path, Backup(path), true);
            }
            catch (Exception e) when (e is PlatformNotSupportedException || e is IOException)
            {
                // 바꿔 넣기를 못 하는 플랫폼·파일 시스템(exFAT 등): 옛 파일을 .bak으로 옮긴 뒤 새 파일을 그 자리로(사이에 꺼져도 .bak이 남는다).
                // Replace가 옛 파일을 이미 .bak으로 옮기고 멈췄으면 그 .bak(유일한 옛 저장)은 건드리지 않는다.
                if (File.Exists(path))
                {
                    if (File.Exists(Backup(path))) File.Delete(Backup(path));
                    File.Move(path, Backup(path));
                }
                File.Move(temp, path);
            }
        }

        // 저장이 있는가: 파일이 있거나, 바꿔 넣는 사이에 꺼져 바로 전 저장(.bak)만 남았다.
        public static bool Exists(string path) => File.Exists(path) || File.Exists(Backup(path));

        // 읽은 글. 파일이 없으면 null, 비었으면(공백뿐) ""(깨진 것 — 부르는 쪽이 .bak을 본다).
        public static string ReadAllText(string path)
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path, Encoding.UTF8);
            return string.IsNullOrWhiteSpace(text) ? "" : text;
        }

        // 파일과 그 .bak·.tmp를 함께 지운다(슬롯 지우기·새 판 — 지운 저장이 .bak에서 되살아나지 않게).
        public static void Delete(string path)
        {
            foreach (var file in new[] { path, Backup(path), Temp(path) })
                if (File.Exists(file)) File.Delete(file);
        }
    }
}
