using Shared.JsonNS;
using SkillEnums;

namespace ShogunCheat
{
    public class Settings
    {
        [JsonIgnore] public string? FilePath;

        [JsonInclude] public int Version = 1;
        [JsonInclude] public List<SkillEnum> BeginRunWithSkills = [];

        public void Save()
        {
            JsonTool.SerializeFile(FilePath!, this);
        }

        public static Settings Load()
        {
            string filePath = Path.Combine(BepInEx.Paths.ConfigPath, "ShogunCheat.json");
            if (JsonTool.DeserializeFile(filePath, out _state))
            {
                _state.FilePath = filePath;
            }
            else
            {
                _state = new();
                _state.FilePath = filePath;
                _state.Save();
            }
            return _state;
        }

        private static Settings? _state;
        public static Settings State => _state ?? Load();
    }
}
