using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Academical
//TODO: We really shouldn't do it this way as it causes us to need to add this SO to every object where we need a lookup, then define
//what is in the lookup. In the future we should use addressables and a file naming convention to make this generic and clean.
{
    [CreateAssetMenu( menuName = "Catalogs/Character Sprite Catalog", fileName = "CharacterProfileLookup" )]
    public class CharacterProfileLookup : ScriptableObject
    {

        [Serializable]
        public class PortraitEntry
        {
            public Sprite Sprite;
            public CharacterSO CharacterSO;
        }

        public List<PortraitEntry> PortraitEntries = new();

        private Color defaultColor = Color.white;

        Dictionary<string, Sprite> _spriteCache;
        Dictionary<string, CharacterSO> _characterSOCache;
        //TODO: Should split this into two cache methods instead of complex conditionals.
        void BuildCache()
        {
            bool initSpriteCache = (_spriteCache == null);
            bool initCharacterSOCache = (_characterSOCache == null);
            if ( initSpriteCache || initCharacterSOCache )
            {
                //Init cache if they don't exist
                if ( initSpriteCache )
                {
                    _spriteCache = new Dictionary<string, Sprite>( StringComparer.OrdinalIgnoreCase );
                }

                if ( initCharacterSOCache )
                { 
                    _characterSOCache = new Dictionary<string, CharacterSO>( StringComparer.OrdinalIgnoreCase );
                }

                //match character names to portrait entries depending on init needs
                foreach ( var entry in PortraitEntries )
                {
                    if ( initSpriteCache )
                    {
                        if ( !string.IsNullOrEmpty( entry.CharacterSO.uid ) && entry.Sprite )
                        {
                            _spriteCache[entry.CharacterSO.uid] = entry.Sprite;
                        }
                    }

                    if ( initCharacterSOCache )
                    { 
                        if ( !string.IsNullOrEmpty( entry.CharacterSO.uid ) && entry.CharacterSO )
                        {
                            _characterSOCache[entry.CharacterSO.uid] = entry.CharacterSO;
                        }                        
                    }
                }
            }
        }

        public bool TryGetSprite(string name, out Sprite sprite)
        {
            BuildCache();
            return _spriteCache.TryGetValue( name, out sprite );
        }

        public bool TryGetBackground(string name, out Color color)
        {
            BuildCache();
            CharacterSO characterSO;
            _characterSOCache.TryGetValue( name, out characterSO );
            if ( characterSO != null )
            {
                color = characterSO.defaultBackgroundColor;
                return true;
            }
            else
            {
                color = defaultColor;
                return false;
            }
            
        }
    }
}