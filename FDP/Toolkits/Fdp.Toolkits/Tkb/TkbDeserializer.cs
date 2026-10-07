using System;
using System.Text.Json;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Vfs;

namespace Fdp.Toolkit.Tkb
{
    /// <summary>
    /// Parses a TKB entity JSON file and registers the resulting TkbTemplate
    /// in an ITkbDatabase.
    /// </summary>
    public sealed class TkbDeserializer
    {
        /// <summary>
        /// Parses the JSON content from <paramref name="file"/>, builds a
        /// <see cref="TkbTemplate"/>, and registers it in <paramref name="db"/>.
        /// </summary>
        /// <exception cref="TkbFormatException">
        /// Thrown if the JSON root element is missing the required <c>$guid</c> field.
        /// </exception>
        public void ParseAndRegister(TkbEntityFile file, ITkbDatabase db)
        {
            using var doc = JsonDocument.Parse(file.JsonStream);
            var root = doc.RootElement;

            // $guid is mandatory -- fail fast.
            if (!root.TryGetProperty("$guid", out var guidProp))
                throw new TkbFormatException(
                    $"Entity '{file.FileName}' in '{file.CategoryPath}' is missing $guid.");
            long tkbId = guidProp.GetInt64();

            var template = new TkbTemplate(file.FileName, tkbId, file.CategoryPath);

            foreach (var prop in root.EnumerateObject())
            {
                ReadOnlySpan<char> name = prop.Name;

                // Skip reserved metadata fields: anything starting with a non-letter
                // (covers $guid, $schema, _EditorMetadata, etc.)
                if (name.IsEmpty || !char.IsLetter(name[0])) continue;

                // Split "Gen.AmmoWeaponBallistics#2" into key="Gen.AmmoWeaponBallistics" + partId=2.
                int hashIdx = name.IndexOf('#');
                ReadOnlySpan<char> key = hashIdx < 0 ? name : name[..hashIdx];
                int partId = 0;
                if (hashIdx >= 0 && hashIdx + 1 < name.Length)
                    int.TryParse(name[(hashIdx + 1)..], out partId);

                // Dispatch to the registered parser thunk; silently skip unknown keys.
                if (TkbDescriptorRegistry.TryGetParser(key, out var thunk) && thunk != null)
                    thunk(template, partId, prop.Value);
            }

            // ⭐ CE-1017 S0 — the master's DIS type reaches the template (what stamps the entity header and drives the map
            //   layers and the Add Entity picker's grouping). ⛔ Before, a TKB file's "DisType" was parsed into the DTO and
            //   never copied, so every file-loaded type was DIS 0. A malformed string fails fast, like a missing $guid.
            var master = template.GetDescriptor<Fdp.Toolkit.Tkb.Domain.TkbMasterDto>();
            if (master != null && !string.IsNullOrWhiteSpace(master.DisType))
            {
                if (!Fdp.Core.DISEntityType.TryParse(master.DisType, out var dis))
                    throw new TkbFormatException(
                        $"Entity '{file.FileName}' in '{file.CategoryPath}': TkbMaster.DisType '{master.DisType}' is not a " +
                        "DIS entity type (Kind.Domain.Country.Category.Subcategory.Specific.Extra).");
                template.DisType = dis;
            }

            db.Register(template);
        }
    }
}
