using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Spatial.Eqs;
using ImGuiApi = ImGuiNET.ImGui;

namespace Hrot.IG.Gizmos
{
    // Renders EqsCognitiveBuffer in the entity inspector panel as a formatted table.
    // Replaces the raw byte-dump default with a readable Rank/EntityId/Position/Score view.
    [ImGuiRenderer(typeof(EqsCognitiveBuffer))]
    public sealed class EqsCognitiveBufferRenderer : IImGuiRenderer
    {
        public string? GetSummary(object value)
        {
            var buf = (EqsCognitiveBuffer)value;
            return buf.IsReady
                ? string.Format("Ready ({0} candidates)", buf.Count)
                : "Awaiting Results...";
        }

        public bool RenderValue(object value)
        {
            var buf = (EqsCognitiveBuffer)value;

            ImGuiApi.TextUnformatted(string.Format("Last Update Tick : {0}", buf.LastUpdateTick));

            if (buf.Count > 0 && ImGuiApi.BeginTable("EqsResultsTable", 4,
                ImGuiNET.ImGuiTableFlags.Borders | ImGuiNET.ImGuiTableFlags.RowBg))
            {
                ImGuiApi.TableSetupColumn("Rank",     ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 40f);
                ImGuiApi.TableSetupColumn("EntityId", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
                ImGuiApi.TableSetupColumn("Position", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
                ImGuiApi.TableSetupColumn("Score",    ImGuiNET.ImGuiTableColumnFlags.WidthFixed,  70f);
                ImGuiApi.TableHeadersRow();

                for (int i = 0; i < buf.Count; i++)
                {
                    var res = buf.GetSpanRO()[i];
                    ImGuiApi.TableNextRow();
                    ImGuiApi.TableSetColumnIndex(0);
                    ImGuiApi.TextUnformatted(string.Format("#{0}", i + 1));
                    ImGuiApi.TableSetColumnIndex(1);
                    ImGuiApi.TextUnformatted(res.EntityId == 0
                        ? "Positional"
                        : res.EntityId.ToString());
                    ImGuiApi.TableSetColumnIndex(2);
                    ImGuiApi.TextUnformatted(string.Format("({0:F1}, {1:F1})", res.PositionX, res.PositionY));
                    ImGuiApi.TableSetColumnIndex(3);
                    ImGuiApi.TextUnformatted(string.Format("{0:F3}", res.Score));
                }

                ImGuiApi.EndTable();
            }

            return true;
        }
    }
}
