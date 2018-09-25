﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using TilemapGenerator.Settings;
using Random = UnityEngine.Random;
using UnityEngine.U2D;

namespace TilemapGenerator.Behaviours
{
    [System.Serializable]
    public class SpawnerProbability
    {
        public InstancedSpawnerConfiguration Spawer;
        [Range(0, 1)]
        public float Probability = 0.5f;
        // @TODO
        // public int MaxCount = -1;
    }

    [System.Serializable]
    public class BiomeConfig
    {
        public TileConfiguration TileConfig;
        [Range(0, 1)]
        public float Height;
        public SpawnerProbability[] Spawners = new SpawnerProbability[0];
    }

    public enum RendererType
    {
        Sorted,
        Unsorted
    }

    [ExecuteInEditMode]
    public class LandGenerator : MonoBehaviour
    {
        public float Seed = 100;
        public int Height = 80;
        public float NoiseScale = 120;
        public int ChunkSize = 64;
        [Range(9, 25)]
        public int ActiveTilemaps = 15;
        public AnimationCurve TerrainCurve = new AnimationCurve();
        public BiomeConfig[] BiomeConfigs = new BiomeConfig[0];
        public Dictionary<float, Tuple<int, Dictionary<Vector4, TileBase>>> CachedBiomes = new Dictionary<float, Tuple<int, Dictionary<Vector4, TileBase>>>();
        public Material TilemapMaterial;
        public Transform Output;
        public ChunkProvider ChunkProvider;
        public Camera MainCamera;
        public Camera MinimapCamera;
        public RendererType RendererType = RendererType.Unsorted;
        public bool AutoGenerate;
        [HideInInspector]
        public Vector3Int RandomOffset = Vector3Int.zero;
        [HideInInspector]
        public Shader SortedShader, IndirectShader;
        public Dictionary<int, ITileRenderer> CachedRenderers = new Dictionary<int, ITileRenderer>();

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Application.targetFrameRate = 0;
                QualitySettings.vSyncCount = 0;
            }
#endif
            Application.targetFrameRate = -1;
            Generate();
        }

        public void Generate()
        {
            Clear();
            Random.InitState(Seed.GetHashCode());
            RandomOffset = new Vector3Int(
                Random.Range(-999999, 999999),
                Random.Range(-999999, 999999),
                0
            );
            foreach (var biome in BiomeConfigs)
            {
                var tiles = new Dictionary<Vector4, TileBase>();
                biome.TileConfig.GetCacheData(tiles);
                float key = Mathf.Round(biome.Height * Height);
                if (!CachedBiomes.ContainsKey(key))
                    CachedBiomes.Add(key, new Tuple<int, Dictionary<Vector4, TileBase>>(biome.TileConfig.GetHashCode(), tiles));
                foreach (var spawner in biome.Spawners)
                {
                    if (spawner.Spawer == null) continue;
                    RegisterSpawner(spawner.Probability, spawner.Spawer);
                }
            }
            ChunkProvider.Boot();
            ChunkProvider.RandomOffset = new Vector3Int(RandomOffset.x, RandomOffset.y, 0);
        }

        private void Clear()
        {
            CachedBiomes.Clear();
            foreach (var renderer in CachedRenderers)
            {
                renderer.Value.Dispose();
            }
            CachedRenderers.Clear();
        }

        private void RegisterSpawner(float probability, InstancedSpawnerConfiguration configuration)
        {
            if (!CachedRenderers.ContainsKey(configuration.GetHashCode()))
            {
                // webgl instancing broken on 2013.x
#if UNITY_WEBGL && !UNITY_EDITOR
                var renderer = new SortedRenderer(MainCamera, MinimapCamera, (int) (probability * ChunkSize * ChunkSize * ActiveTilemaps), configuration.PackedTexture, SortedShader, configuration.MeshSize);
                CachedRenderers.Add(configuration.GetHashCode(), renderer);
#else
                if (RendererType == RendererType.Sorted)
                {
                    var renderer = new SortedRenderer(MainCamera, MinimapCamera, (int) (probability * ChunkSize * ChunkSize * ActiveTilemaps), configuration.PackedTexture, SortedShader, configuration.MeshSize);
                    CachedRenderers.Add(configuration.GetHashCode(), renderer);
                }
                else
                {
                    var renderer = new InstancedIndirectRenderer(MainCamera, MinimapCamera, (int) (probability * ChunkSize * ChunkSize * ActiveTilemaps), configuration.PackedTexture, IndirectShader, configuration.MeshSize);
                    CachedRenderers.Add(configuration.GetHashCode(), renderer);
                }
#endif
            }
        }

        private void LateUpdate()
        {
            foreach (var renderer in CachedRenderers)
            {
                renderer.Value.Tick();
            }
        }

        private void OnDisable()
        {
            Clear();
        }

        public Vector3 MapToWorld(Vector3 map)
        {
            float chunkSize = ChunkSize;
            float halfSize = chunkSize / 2f;
            Vector3 worldPos = Vector3.zero;
            worldPos.x = (map.x / chunkSize - map.y / chunkSize) * halfSize;
            worldPos.y = (map.x / chunkSize + map.y / chunkSize) * halfSize / 2f - halfSize / 2f;
            worldPos.z = -map.z;
            return worldPos;
        }

        public Vector3 WorldToMap(Vector3 worldPosition)
        {
            float halfCellWidth = 1f / 2f;
            float halfCellHeight = 1f / 4f;
            Vector3 mapPosition = Vector3.zero;
            mapPosition.x = (worldPosition.x / halfCellWidth + worldPosition.y / halfCellHeight) / 2 + ChunkSize / 2f;
            mapPosition.y = (worldPosition.y / halfCellHeight - (worldPosition.x / halfCellWidth)) / 2 + ChunkSize / 2f;
            return mapPosition;
        }

        public float SampleMapHeight(Vector2 mapPosition)
        {
            Vector2 randomOffset = new Vector2(RandomOffset.x, RandomOffset.y);
            Vector2 samplePoint = (randomOffset + mapPosition) / NoiseScale;
            float perlinValue = Mathf.PerlinNoise(samplePoint.x, samplePoint.y);
            return Mathf.Round(TerrainCurve.Evaluate(perlinValue) * Height);
        }

        public float SampleWorldHeight(Vector2 worldPosition)
        {
            Vector3 mapPosition = WorldToMap(worldPosition);
            return SampleMapHeight(mapPosition);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Height < 1)
            {
                Height = 1;
            }
            if (NoiseScale < 0)
            {
                NoiseScale = 0;
            }
        }
#endif
    }
}
