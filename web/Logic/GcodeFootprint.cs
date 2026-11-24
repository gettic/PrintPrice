using System;
using System.Globalization;

namespace PrintPrice.Web
{
    /// Batch-plates: the space a print really takes on the plate, from its G-code (fed line by line with the prime tower
    /// count, so no extra read). A sliced file's Metadata/plate_N.json only has each object's FIRST-LAYER box (a Benchy:
    /// 39 x 16 mm instead of 60 x 31), so copies-per-plate from it would count about 3x too many.
    /// Counts every extruding move (start and end point) inside a model feature ("; FEATURE: X" Bambu / Orca,
    /// ";TYPE:X" Prusa / Cura): towers apart (their own box), skirt and custom start / end code left out, Bambu's
    /// filament-change blocks (M620 .. M621, the purge at the chute) left out, points below 0 left out (off the plate).
    /// Arcs count by their end points (a few tenths of a mm short at most). Pure; never throws.
    public sealed class GcodeFootprint
    {
        double x, y, e;
        bool absE, relXY, inChange;
        int feature;   // 0 = none / left out, 1 = model, 2 = tower
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        double tMinX = double.MaxValue, tMinY = double.MaxValue, tMaxX = double.MinValue, tMaxY = double.MinValue;

        public void Feed(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            try { FeedLine(line); } catch (Exception) { }
        }

        void FeedLine(string line)
        {
            char c0 = line[0];
            if (c0 == ';')
            {
                string t = line.TrimStart(';', ' ');
                string kind = null;
                if (t.StartsWith("FEATURE:", StringComparison.Ordinal)) kind = t.Substring(8);
                else if (t.StartsWith("TYPE:", StringComparison.Ordinal)) kind = t.Substring(5);
                if (kind == null) return;
                kind = kind.Trim();
                if (kind.IndexOf("tower", StringComparison.OrdinalIgnoreCase) >= 0) feature = 2;
                else if (kind.IndexOf("skirt", StringComparison.OrdinalIgnoreCase) >= 0 || kind.IndexOf("custom", StringComparison.OrdinalIgnoreCase) >= 0
                         || kind.Length == 0) feature = 0;
                else feature = 1;
                return;
            }
            if (c0 == 'M' || c0 == 'm')
            {
                if (Code(line, "M620")) inChange = true;
                else if (Code(line, "M621")) inChange = false;
                else if (Code(line, "M82")) absE = true;
                else if (Code(line, "M83")) absE = false;
                return;
            }
            if (c0 != 'G' && c0 != 'g') return;
            if (Code(line, "G90")) { relXY = false; return; }
            if (Code(line, "G91")) { relXY = true; return; }
            if (Code(line, "G92")) { double v; if (Value(line, 'E', out v)) e = v; return; }
            bool move = Code(line, "G1") || Code(line, "G0") || Code(line, "G2") || Code(line, "G3");
            if (!move) return;
            double nx = x, ny = y, ne, v2;
            if (Value(line, 'X', out v2)) nx = relXY ? x + v2 : v2;
            if (Value(line, 'Y', out v2)) ny = relXY ? y + v2 : v2;
            bool extrudes = false;
            if (Value(line, 'E', out ne))
            {
                extrudes = absE ? ne > e + 1e-9 : ne > 1e-9;
                if (absE) e = ne;
            }
            if (extrudes && !inChange && feature != 0 && (nx != x || ny != y))
            {
                Add(x, y);
                Add(nx, ny);
            }
            x = nx; y = ny;
        }

        void Add(double px, double py)
        {
            if (px < 0 || py < 0 || double.IsNaN(px) || double.IsNaN(py)) return;
            if (feature == 2)
            {
                tMinX = Math.Min(tMinX, px); tMinY = Math.Min(tMinY, py); tMaxX = Math.Max(tMaxX, px); tMaxY = Math.Max(tMaxY, py);
            }
            else
            {
                minX = Math.Min(minX, px); minY = Math.Min(minY, py); maxX = Math.Max(maxX, px); maxY = Math.Max(maxY, py);
            }
        }

        /// The footprint (Objects = 0: not counted here), null when no model extrusion was seen.
        public PlateFootprint Result()
        {
            if (!(maxX > minX) || !(maxY > minY)) return null;
            var fp = new PlateFootprint { Width = Math.Round(maxX - minX, 3), Depth = Math.Round(maxY - minY, 3) };
            if (tMaxX > tMinX && tMaxY > tMinY) { fp.TowerWidth = Math.Round(tMaxX - tMinX, 3); fp.TowerDepth = Math.Round(tMaxY - tMinY, 3); }
            return fp;
        }

        /// The plate JSON's footprint (first layer, + its tower box) widened by the G-code's (the whole height); either may be null.
        public static PlateFootprint Combine(PlateFootprint fromJson, PlateFootprint fromGcode)
        {
            if (fromGcode == null) return fromJson;
            if (fromJson == null) return fromGcode;
            return new PlateFootprint
            {
                Width = Math.Max(fromJson.Width, fromGcode.Width), Depth = Math.Max(fromJson.Depth, fromGcode.Depth),
                TowerWidth = fromJson.TowerWidth > 0 ? fromJson.TowerWidth : fromGcode.TowerWidth,
                TowerDepth = fromJson.TowerDepth > 0 ? fromJson.TowerDepth : fromGcode.TowerDepth,
                Objects = fromJson.Objects
            };
        }

        // "G1" matches "G1 X.." / "G1" but not "G10" / "G11"
        static bool Code(string line, string code)
        {
            if (line.Length < code.Length || string.Compare(line, 0, code, 0, code.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
            if (line.Length == code.Length) return true;
            char n = line[code.Length];
            return n == ' ' || n == '\t' || n == ';';
        }

        // the number after " X" (up to the next space / ';'), invariant
        static bool Value(string line, char letter, out double v)
        {
            v = 0;
            int end = line.IndexOf(';');
            if (end < 0) end = line.Length;
            for (int i = 1; i < end; i++)
            {
                char ch = line[i];
                if ((ch == letter || ch == char.ToLowerInvariant(letter)) && (line[i - 1] == ' ' || line[i - 1] == '\t'))
                {
                    int j = i + 1;
                    while (j < end && line[j] != ' ' && line[j] != '\t') j++;
                    return double.TryParse(line.Substring(i + 1, j - i - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                }
            }
            return false;
        }
    }
}
