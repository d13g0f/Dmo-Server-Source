using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Api.Controllers
{
    [ApiController]
    [Route("v1/[controller]")]
    public class UnstuckController : BaseController
    {
        private readonly DatabaseContext _context;
        private readonly ILogger<UnstuckController> _logger;
        private readonly IConfiguration _configuration;

        // Constantes para zona segura DATS
        private const short SafeMapId = 3;
        private const int SafeIndex = 0;
        private const int DefaultZ = 0;

        public UnstuckController(
            DatabaseContext context,
            ILogger<UnstuckController> logger,
            IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] UnstuckRequest request)
        {
            // 1. Autenticación token
            var token = GetToken();
            if (token != _configuration["Authentication:TokenKey"] )
            {
                _logger.LogWarning("Unstuck failed: invalid token {Token}", token);
                return Unauthorized("Invalid token.");
            }

            // 2. Validar parámetros
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.TamerName))
            {
                _logger.LogWarning("Unstuck failed: missing parameters");
                return BadRequest("Username and TamerName are required.");
            }

            // 3. Obtener cuenta
            _logger.LogInformation("Fetching account for Username: {Username}", request.Username);
            var account = await _context.Account
                .FirstOrDefaultAsync(a => a.Username == request.Username);
            if (account == null)
            {
                _logger.LogWarning("Unstuck failed: account {Username} not found", request.Username);
                return NotFound("Account not found.");
            }

            // 4. Validar DiscordId
            if (request.DiscordID != account.DiscordId)
            {
                _logger.LogWarning("Unstuck failed: discord mismatch for account {Username}", request.Username);
                _logger.LogInformation("Comparing DiscordIDs -> FromRequest: {DiscordIDRequest} | FromAccount: {DiscordIDAccount}",
                request.DiscordID, account.DiscordId);
                return Unauthorized("DiscordId does not match.");
            }

            // 5. Obtener tamer
            _logger.LogInformation("Fetching tamer for TamerName: {TamerName}", request.TamerName);
            var tamer = await _context.Character
                .FirstOrDefaultAsync(c => c.Name == request.TamerName && c.AccountId == account.Id);
            if (tamer == null)
            {
                _logger.LogWarning("Unstuck failed: tamer {TamerName} not found or not in account {Username}", request.TamerName, request.Username);
                return NotFound("Tamer not found.");
            }

            // 6. Verificar estado desconectado
            if (tamer.State != CharacterStateEnum.Disconnected)
            {
                _logger.LogWarning("Unstuck failed: tamer {TamerName} is not offline", request.TamerName);
                return BadRequest("Tamer must be offline.");
            }

            // 7. Obtener coordenadas seguras
            const int safeX = 19993;
            const int safeY = 15116;

            // 8. Upsert ubicación del tamer
            await EnsureCharacterLocationAsync(tamer.Id, safeX, safeY, DefaultZ, SafeMapId);

            // 9. Upsert ubicación del digimon activo
            var activeDigimon = await _context.Digimon
                .FirstOrDefaultAsync(d => d.CharacterId == tamer.Id && d.Slot == 0);
            if (activeDigimon != null)
            {
                await EnsureDigimonLocationAsync(activeDigimon.Id, safeX, safeY, DefaultZ, SafeMapId);
            }
            else
            {
                _logger.LogInformation("No active digimon for tamer {TamerName}", request.TamerName);
            }

            // 10. Guardar cambios
            await _context.SaveChangesAsync();

            _logger.LogInformation("Unstuck executed: {TamerName} moved to safe zone", request.TamerName);
            return Ok(new { Result = HttpStatusCode.OK });
        }

       
        // Inserta o actualiza la ubicación de un Character
        private async Task EnsureCharacterLocationAsync(long characterId, int x, int y, int z, short mapId)
        {
            var location = await _context.CharacterLocation
                .FirstOrDefaultAsync(cl => cl.CharacterId == characterId);

            if (location == null)
            {
                location = new Commons.DTOs.Character.CharacterLocationDTO
                {
                    CharacterId = characterId,
                    MapId = mapId,
                    X = x,
                    Y = y,
                    Z = z
                };
                _context.CharacterLocation.Add(location);
            }
            else
            {
                location.MapId = mapId;
                location.X = x;
                location.Y = y;
                location.Z = z;
                _context.CharacterLocation.Update(location);
            }
        }

        // Inserta o actualiza la ubicación de un Digimon
        private async Task EnsureDigimonLocationAsync(long digimonId, int x, int y, int z, short mapId)
        {
            var loc = await _context.DigimonLocation
                .FirstOrDefaultAsync(dl => dl.DigimonId == digimonId);

            if (loc == null)
            {
                loc = new Commons.DTOs.Digimon.DigimonLocationDTO
                {
                    DigimonId = digimonId,
                    MapId = mapId,
                    X = x,
                    Y = y,
                    Z = z
                };
                _context.DigimonLocation.Add(loc);
            }
            else
            {
                loc.MapId = mapId;
                loc.X = x;
                loc.Y = y;
                loc.Z = z;
                _context.DigimonLocation.Update(loc);
            }
        }
    }

    public class UnstuckRequest
    {
        public string Username { get; set; }
        public string TamerName { get; set; }
        public string DiscordID { get; set; }
    }
}
