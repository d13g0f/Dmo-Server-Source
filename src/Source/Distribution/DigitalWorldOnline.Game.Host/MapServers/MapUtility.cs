using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Models.Map;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DigitalWorldOnline.Commons.Utils
{
    /// <summary>
    /// Utility class for map-related targeting and range calculations, shared across map types.
    /// </summary>
    public static class MapUtility
    {
        /// <summary>
        /// Calculates Euclidean distance between two points.
        /// </summary>
        /// <param name="x1">X-coordinate of the first point.</param>
        /// <param name="y1">Y-coordinate of the first point.</param>
        /// <param name="x2">X-coordinate of the second point.</param>
        /// <param name="y2">Y-coordinate of the second point.</param>
        /// <returns>Distance between the points.</returns>
        public static double CalculateDistance(int x1, int y1, int x2, int y2)
        {
            var deltaX = x2 - x1;
            var deltaY = y2 - y1;
            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        /// <summary>
        /// Filters entities within a circular area based on range.
        /// </summary>
        /// <typeparam name="T">Type of entity (e.g., DigimonModel, IMob).</typeparam>
        /// <param name="entities">List of entities to filter.</param>
        /// <param name="originX">X-coordinate of the center.</param>
        /// <param name="originY">Y-coordinate of the center.</param>
        /// <param name="range">Radius of the circular area.</param>
        /// <returns>List of entities within the range.</returns>
        public static List<T> CalculateCircularRange<T>(List<T> entities, int originX, int originY, int range)
            where T : class
        {
            var result = new List<T>();
            foreach (var entity in entities)
            {
                var entityX = entity is DigimonModel digimon ? digimon.Location.X :
                             entity is IMob mob ? mob.CurrentLocation.X : 0;
                var entityY = entity is DigimonModel digimon2 ? digimon2.Location.Y :
                             entity is IMob mob2 ? mob2.CurrentLocation.Y : 0;
                var distance = CalculateDistance(originX, originY, entityX, entityY);
                if (distance <= range)
                {
                    result.Add(entity);
                }
            }
            return result;
        }

        /// <summary>
        /// Placeholder for linear AoE range calculation (e.g., ray-based effects).
        /// </summary>
        /// <typeparam name="T">Type of entity.</typeparam>
        /// <param name="entities">List of entities to filter.</param>
        /// <param name="originX">X-coordinate of the start point.</param>
        /// <param name="originY">Y-coordinate of the start point.</param>
        /// <param name="directionX">X-coordinate of the direction vector.</param>
        /// <param name="directionY">Y-coordinate of the direction vector.</param>
        /// <param name="range">Length of the linear area.</param>
        /// <returns>List of entities along the line (to be implemented).</returns>
        public static List<T> CalculateLinearRange<T>(List<T> entities, int originX, int originY, int directionX, int directionY, int range)
            where T : class
        {
            // TODO: Implement linear AoE logic (e.g., check entities along a line from origin to direction).
            // For now, return empty list as placeholder.
            return new List<T>();
        }

        /// <summary>
        /// Checks if two Tamers are allies based on party or guild membership.
        /// </summary>
        /// <param name="source">Source Tamer's GameClient.</param>
        /// <param name="target">Target Tamer's GameClient.</param>
        /// <returns>True if allies (same party or guild).</returns>
        public static bool IsAlly(GameClient source, GameClient target)
        {
            if (source?.Tamer == null || target?.Tamer == null) return false;
            return (source.PartyId > 0 && source.PartyId == target.PartyId) ||
                   (source.Tamer.Guild?.Id > 0 && source.Tamer.Guild.Id == target.Tamer.Guild?.Id);
        }

        /// <summary>
        /// Gets enemy Digimons within range of the attacker's Digimon (PvP AoE).
        /// </summary>
        /// <param name="map">Target map.</param>
        /// <param name="location">Attacker's Digimon location.</param>
        /// <param name="range">AoE range.</param>
        /// <param name="tamerId">Attacker's Tamer ID.</param>
        /// <returns>List of enemy Digimons within range.</returns>
        public static List<DigimonModel> GetEnemiesNearbyPartner(GameMap map, Location location, int range, long tamerId)
        {
            if (map == null || map.Type != MapTypeEnum.Pvp) return new List<DigimonModel>();

            var sourceClient = map.Clients.FirstOrDefault(c => c.TamerId == tamerId);
            if (sourceClient?.Tamer == null) return new List<DigimonModel>();

            var digimons = map.ConnectedTamers
                .Select(t => t.Partner)
                .Where(d => d != null && d.Alive && d.GeneralHandler != sourceClient.Tamer.Partner.GeneralHandler)
                .ToList();

            var enemies = CalculateCircularRange(digimons, location.X, location.Y, range)
                .Where(d => !IsAlly(sourceClient, map.Clients.FirstOrDefault(c => c.Tamer.Partner.GeneralHandler == d.GeneralHandler)))
                .DistinctBy(d => d.Id)
                .ToList();

            return enemies;
        }

        /// <summary>
        /// Gets enemy Digimons within range of a target Digimon (PvP AoE).
        /// </summary>
        /// <param name="map">Target map.</param>
        /// <param name="mapId">Map ID.</param>
        /// <param name="handler">Target Digimon's handler.</param>
        /// <param name="range">AoE range.</param>
        /// <param name="tamerId">Attacker's Tamer ID.</param>
        /// <returns>List of enemy Digimons including the target.</returns>
        public static List<DigimonModel> GetEnemiesNearbyTarget(GameMap map, short mapId, int handler, int range, long tamerId)
        {
            if (map == null || map.Type != MapTypeEnum.Pvp) return new List<DigimonModel>();

            var sourceClient = map.Clients.FirstOrDefault(c => c.TamerId == tamerId);
            if (sourceClient?.Tamer == null) return new List<DigimonModel>();

            var targetDigimon = map.ConnectedTamers
                .Select(t => t.Partner)
                .FirstOrDefault(d => d.GeneralHandler == handler && d.Alive);
            if (targetDigimon == null) return new List<DigimonModel>();

            var digimons = map.ConnectedTamers
                .Select(t => t.Partner)
                .Where(d => d != null && d.Alive)
                .ToList();

            var enemies = CalculateCircularRange(digimons, targetDigimon.Location.X, targetDigimon.Location.Y, range / 5) // Keep range / 5 for consistency
                .Where(d => !IsAlly(sourceClient, map.Clients.FirstOrDefault(c => c.Tamer.Partner.GeneralHandler == d.GeneralHandler)))
                .DistinctBy(d => d.Id)
                .ToList();

            if (!enemies.Contains(targetDigimon) && !IsAlly(sourceClient, map.Clients.FirstOrDefault(c => c.Tamer.Partner.GeneralHandler == targetDigimon.GeneralHandler)))
                enemies.Add(targetDigimon);

            return enemies;
        }

        /// <summary>
        /// Gets allied Digimons within range of the attacker's Digimon (PvP buffs/auras).
        /// </summary>
        /// <param name="map">Target map.</param>
        /// <param name="location">Attacker's Digimon location.</param>
        /// <param name="range">AoE range.</param>
        /// <param name="tamerId">Attacker's Tamer ID.</param>
        /// <returns>List of allied Digimons within range.</returns>
        public static List<DigimonModel> GetAlliesNearbyPartner(GameMap map, Location location, int range, long tamerId)
        {
            if (map == null || map.Type != MapTypeEnum.Pvp) return new List<DigimonModel>();

            var sourceClient = map.Clients.FirstOrDefault(c => c.TamerId == tamerId);
            if (sourceClient?.Tamer == null) return new List<DigimonModel>();

            var digimons = map.ConnectedTamers
                .Select(t => t.Partner)
                .Where(d => d != null && d.Alive)
                .ToList();

            var allies = CalculateCircularRange(digimons, location.X, location.Y, range)
                .Where(d => IsAlly(sourceClient, map.Clients.FirstOrDefault(c => c.Tamer.Partner.GeneralHandler == d.GeneralHandler)))
                .DistinctBy(d => d.Id)
                .ToList();

            return allies;
        }

        /// <summary>
        /// Gets allied Digimons within range of a target Digimon (PvP buffs/auras).
        /// </summary>
        /// <param name="map">Target map.</param>
        /// <param name="mapId">Map ID.</param>
        /// <param name="handler">Target Digimon's handler.</param>
        /// <param name="range">AoE range.</param>
        /// <param name="tamerId">Attacker's Tamer ID.</param>
        /// <returns>List of allied Digimons including the target if allied.</returns>
        public static List<DigimonModel> GetAlliesNearbyTarget(GameMap map, short mapId, int handler, int range, long tamerId)
        {
            if (map == null || map.Type != MapTypeEnum.Pvp) return new List<DigimonModel>();

            var sourceClient = map.Clients.FirstOrDefault(c => c.TamerId == tamerId);
            if (sourceClient?.Tamer == null) return new List<DigimonModel>();

            var targetDigimon = map.ConnectedTamers
                .Select(t => t.Partner)
                .FirstOrDefault(d => d.GeneralHandler == handler && d.Alive);
            if (targetDigimon == null) return new List<DigimonModel>();

            var digimons = map.ConnectedTamers
                .Select(t => t.Partner)
                .Where(d => d != null && d.Alive)
                .ToList();

            var allies = CalculateCircularRange(digimons, targetDigimon.Location.X, targetDigimon.Location.Y, range / 5) // Keep range / 5
                .Where(d => IsAlly(sourceClient, map.Clients.FirstOrDefault(c => c.Tamer.Partner.GeneralHandler == d.GeneralHandler)))
                .DistinctBy(d => d.Id)
                .ToList();

            if (IsAlly(sourceClient, map.Clients.FirstOrDefault(c => c.Tamer.Partner.GeneralHandler == targetDigimon.GeneralHandler)) &&
                !allies.Contains(targetDigimon))
                allies.Add(targetDigimon);

            return allies;
        }

        /// <summary>
        /// Gets a single enemy Digimon by handler for PvP single-target skills.
        /// </summary>
        /// <param name="map">Target map.</param>
        /// <param name="mapId">Map ID.</param>
        /// <param name="handler">Target Digimon's handler.</param>
        /// <param name="tamerId">Attacker's Tamer ID.</param>
        /// <returns>Enemy Digimon or null if not found or allied.</returns>
        public static DigimonModel? GetEnemyByHandler(GameMap map, short mapId, int handler, long tamerId)
        {
            if (map == null || map.Type != MapTypeEnum.Pvp) return null;

            var sourceClient = map.Clients.FirstOrDefault(c => c.TamerId == tamerId);
            if (sourceClient?.Tamer == null) return null;

            var targetDigimon = map.ConnectedTamers
                .Select(t => t.Partner)
                .FirstOrDefault(d => d.GeneralHandler == handler && d.Alive);

            if (targetDigimon == null) return null;

            if (IsAlly(sourceClient, map.Clients.FirstOrDefault(c => c.Tamer.Partner.GeneralHandler == handler))) return null;

            return targetDigimon;
        }

        /// <summary>
        /// Gets mobs within range of the attacker's Digimon (PvE AoE).
        /// </summary>
        /// <typeparam name="T">Type of mob (e.g., MobConfigModel, SummonMobModel).</typeparam>
        /// <param name="map">Target map.</param>
        /// <param name="location">Attacker's Digimon location.</param>
        /// <param name="range">AoE range.</param>
        /// <param name="tamerId">Attacker's Tamer ID.</param>
        /// <returns>List of mobs within range.</returns>
        public static List<T> GetMobsNearbyPartner<T>(GameMap map, Location location, int range, long tamerId)
            where T : class, IMob
        {
            if (map == null || !map.Clients.Exists(c => c.TamerId == tamerId)) return new List<T>();

            var mobs = map.IMobs.OfType<T>().Where(m => m.Alive).ToList();
            return CalculateCircularRange(mobs, location.X, location.Y, range)
                .DistinctBy(m => m.Id)
                .ToList();
        }

        /// <summary>
        /// Gets mobs within range of a target mob (PvE AoE).
        /// </summary>
        /// <typeparam name="T">Type of mob.</typeparam>
        /// <param name="map">Target map.</param>
        /// <param name="mapId">Map ID.</param>
        /// <param name="handler">Target mob's handler.</param>
        /// <param name="range">AoE range.</param>
        /// <param name="tamerId">Attacker's Tamer ID.</param>
        /// <returns>List of mobs including the target.</returns>
        public static List<T> GetMobsNearbyTarget<T>(GameMap map, short mapId, int handler, int range, long tamerId)
            where T : class, IMob
        {
            if (map == null || !map.Clients.Exists(c => c.TamerId == tamerId)) return new List<T>();

            var targetMob = map.IMobs.OfType<T>().FirstOrDefault(m => m.GeneralHandler == handler && m.Alive);
            if (targetMob == null) return new List<T>();

            var mobs = map.IMobs.OfType<T>().Where(m => m.Alive).ToList();
            var result = CalculateCircularRange(mobs, targetMob.CurrentLocation.X, targetMob.CurrentLocation.Y, range / 5) // Keep range / 5
                .DistinctBy(m => m.Id)
                .ToList();

            if (!result.Contains(targetMob))
                result.Add(targetMob);

            return result;
        }

        /// <summary>
        /// Gets a single mob by handler for PvE single-target skills.
        /// </summary>
        /// <typeparam name="T">Type of mob.</typeparam>
        /// <param name="map">Target map.</param>
        /// <param name="mapId">Map ID.</param>
        /// <param name="handler">Target mob's handler.</param>
        /// <param name="tamerId">Attacker's Tamer ID.</param>
        /// <returns>Mob or null if not found.</returns>
        public static T? GetMobByHandler<T>(GameMap map, short mapId, int handler, long tamerId)
            where T : IMob
        {
            if (map == null || !map.Clients.Exists(c => c.TamerId == tamerId)) return default;
            return map.IMobs.OfType<T>().FirstOrDefault(m => m.GeneralHandler == handler && m.Alive);
        }
    }
}