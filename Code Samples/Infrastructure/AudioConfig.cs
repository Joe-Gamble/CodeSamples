using Gamble.Utils;
using UnityEngine;

namespace Gamble.BattleCards.Infrastructure
{
    public class AudioConfig : IConfig
    {
        private struct SoundData
        {
            public SoundData(AudioConfig config)
            {
                master = config.masterVolume.Value;
                sfx = config.sfxVolume.Value;
                music = config.musicVolume.Value;
                voice = config.voiceVolume.Value;
            }

            public float master;
            public float sfx;
            public float music;
            public float voice;
        }

        public string FileName => "SoundSettings";

        public Observable<float> masterVolume = new Observable<float>(70f);
        public Observable<float> sfxVolume = new Observable<float>(70f);
        public Observable<float> musicVolume = new Observable<float>(70f);
        public Observable<float> voiceVolume = new Observable<float>(70f);

        public void Deserialize(string json)
        {
            SoundData data = JsonUtility.FromJson<SoundData>(json);

            masterVolume.Value = data.master;
            sfxVolume.Value = data.sfx;
            musicVolume.Value = data.music;
            voiceVolume.Value = data.voice;
        }

        public string Serialize()
        {
            string data = JsonUtility.ToJson(new SoundData(this), true);
            return data;
        }
    }
}
