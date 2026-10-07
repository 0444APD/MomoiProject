using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GBible
{
    // CSV 읽기·쓰기(RFC 4180): 쉼표 구분, 큰따옴표로 감싼 칸 안의 쉼표·줄바꿈·"" 허용.
    // 구글 시트·엑셀에서 내려받은 CSV를 그대로 읽는다(맨 앞 BOM 무시, \r\n·\n 모두 허용).
    // 손으로 쓴 CSV처럼 따옴표로 감싸지 않은 칸 가운데의 "는 글자로 읽는다(엑셀과 같다).
    public static class Csv
    {
        public static List<string[]> Read(string text)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text)) return rows;
            var row = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            var i = text[0] == '﻿' ? 1 : 0;
            var any = false;
            // 이 칸에 공백 아닌 글자나 따옴표 칸이 나왔는가. 따옴표 칸은 칸 맨 앞(공백 뒤)에서만 시작한다 — 가운데의 "는 글자(5" 칼).
            var started = false;

            void EndCell()
            {
                row.Add(cell.ToString());
                cell.Clear();
                started = false;
            }

            void EndRow()
            {
                EndCell();
                // 완전히 빈 줄은 건너뛴다.
                if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
                row.Clear();
                any = false;
            }

            for (; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else quoted = false;
                    }
                    else cell.Append(c);
                    continue;
                }
                switch (c)
                {
                    case '"':
                        if (started) cell.Append(c);
                        else quoted = started = true;
                        any = true;
                        break;
                    case ',':
                        EndCell();
                        any = true;
                        break;
                    case '\r':
                        if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                        EndRow();
                        break;
                    case '\n':
                        EndRow();
                        break;
                    default:
                        cell.Append(c);
                        any = true;
                        if (!char.IsWhiteSpace(c)) started = true;
                        break;
                }
            }
            if (any || cell.Length > 0 || row.Count > 0) EndRow();
            return rows;
        }

        public static string Write(IEnumerable<IEnumerable<string>> rows)
        {
            var b = new StringBuilder();
            foreach (var row in rows)
            {
                b.Append(string.Join(",", row.Select(Escape)));
                b.Append("\r\n");
            }
            return b.ToString();
        }

        public static string Escape(string cell)
        {
            cell ??= "";
            if (cell.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 && cell.Trim() == cell) return cell;
            return "\"" + cell.Replace("\"", "\"\"") + "\"";
        }

        // 머리줄 이름으로 열 찾기(대소문자·앞뒤 공백 무시). 없으면 -1.
        public static int Column(string[] header, string name)
        {
            for (var i = 0; i < header.Length; i++)
                if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public static string Cell(string[] row, int column) => column >= 0 && column < row.Length ? row[column] : "";
    }
}
