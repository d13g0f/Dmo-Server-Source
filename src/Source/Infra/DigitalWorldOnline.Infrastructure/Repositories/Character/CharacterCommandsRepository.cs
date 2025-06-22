using System.Reflection;
using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.DTOs.Assets;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Mechanics;
using DigitalWorldOnline.Commons.Model.Character;
using DigitalWorldOnline.Commons.Models.Events;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Chat;
using DigitalWorldOnline.Commons.DTOs.Character;
using DigitalWorldOnline.Commons.DTOs.Digimon;
using DigitalWorldOnline.Commons.DTOs.Base;
using DigitalWorldOnline.Commons.DTOs.Chat;
using DigitalWorldOnline.Commons.Models.Asset;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace DigitalWorldOnline.Infrastructure.Repositories.Character
{
    public class CharacterCommandsRepository : ICharacterCommandsRepository
    {
        private readonly DatabaseContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger _logger;

        public CharacterCommandsRepository(DatabaseContext context, IMapper mapper, ILogger logger)
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<long> AddCharacterAsync(CharacterModel character)
        {
            var dto = _mapper.Map<CharacterDTO>(character);

            _context.Character.Add(dto);

            await _context.SaveChangesAsync();

            return dto.Id;
        }

        public async Task<DigimonDTO> AddDigimonAsync(DigimonModel digimon)
        {
            var tamerDto = await _context.Character
                .Include(x => x.Digimons)
                .ThenInclude(y => y.Digiclone)
                .ThenInclude(z => z.History)
                .Include(x => x.Digimons)
                .ThenInclude(y => y.AttributeExperience)
                .Include(x => x.Digimons)
                .ThenInclude(y => y.Location)
                .Include(x => x.Digimons)
                .ThenInclude(y => y.BuffList)
                .ThenInclude(z => z.Buffs)
                .Include(x => x.Digimons)
                .ThenInclude(y => y.Evolutions)
                .ThenInclude(z => z.Skills)
                .Include(x => x.Encyclopedia)
                .ThenInclude(x => x.Evolutions)
                .Include(x => x.Encyclopedia)
                .ThenInclude(y => y.EvolutionAsset)
                .SingleOrDefaultAsync(x => x.Id == digimon.CharacterId);

            try
            {
                var dto = _mapper.Map<DigimonDTO>(digimon);

                if (tamerDto != null)
                {
                    tamerDto.Digimons.Add(dto);

                    _context.SaveChanges();
                }

                return dto;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public async Task<CharacterFriendDTO> AddFriendAsync(CharacterFriendModel friend)
        {
            var tamerDto = await _context.Character
                .AsNoTracking()
                .Include(x => x.Friends)
                .SingleOrDefaultAsync(x => x.Id == friend.CharacterId);

            var dto = _mapper.Map<CharacterFriendDTO>(friend);

            if (tamerDto != null)
            {
                tamerDto.Friends.Add(dto);

                _context.Update(tamerDto);

                _context.SaveChanges();
            }

            return dto;
        }

        public async Task<DeleteCharacterResultEnum> DeleteCharacterByAccountAndPositionAsync(long accountId, byte characterPosition)
        {
            try
            {
                var dto = await _context.Character
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(x => x.Incubator)
                    .Include(x => x.Location)
                    .Include(x => x.Xai)
                    .Include(x => x.TimeReward)
                    .Include(x => x.AttendanceReward)
                    .Include(x => x.ActiveSkill)
                    .Include(x => x.DailyPoints)
                    .Include(x => x.ConsignedShop)
                    .Include(x => x.MapRegions)
                    .Include(x => x.Points)
                    .Include(x => x.BuffList)
                    .ThenInclude(y => y.Buffs)
                    .Include(x => x.SealList)
                    .ThenInclude(y => y.Seals)
                    .Include(x => x.ItemList)
                    .ThenInclude(y => y.Items)
                    .Include(x => x.Digimons)
                    .ThenInclude(y => y.Digiclone)
                    .Include(x => x.Digimons)
                    .ThenInclude(y => y.AttributeExperience)
                    .Include(x => x.Digimons)
                    .ThenInclude(y => y.Location)
                    .Include(x => x.Digimons)
                    .ThenInclude(y => y.BuffList)
                    .ThenInclude(z => z.Buffs)
                    .Include(x => x.Digimons)
                    .ThenInclude(z => z.Evolutions)
                    .SingleOrDefaultAsync(x => x.AccountId == accountId &&
                                               x.Position == characterPosition);

                if (dto != null)
                {
                    _context.Remove(dto);
                    _context.SaveChanges();
                }

                return DeleteCharacterResultEnum.Deleted;
            }
            catch
            {
                return DeleteCharacterResultEnum.Error;
            }
        }

        public async Task UpdateCharacterChannelByIdAsync(long characterId, byte channel)
        {
            var character = await _context.Character
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == characterId);

            if (character != null)
            {
                character.Channel = channel;

                _context.Character.Update(character);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterLocationAsync(CharacterLocationModel location)
        {
            var dto = await _context.CharacterLocation
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == location.Id);

            if (dto != null)
            {
                dto.MapId = location.MapId;
                dto.X = location.X;
                dto.Y = location.Y;
                dto.Z = location.Z;

                _context.CharacterLocation.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateDigimonLocationAsync(DigimonLocationModel location)
        {
            var dto = await _context.DigimonLocation
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == location.Id);

            if (dto != null)
            {
                dto.MapId = location.MapId;
                dto.X = location.X;
                dto.Y = location.Y;
                dto.Z = location.Z;

                _context.DigimonLocation.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterResourcesAsync(CharacterModel tamer)
        {
            var tamerDto = await _context.Character
                .AsNoTracking()
                .Include(x => x.Digimons)
                .FirstOrDefaultAsync(x => x.Id == tamer.Id);

            if (tamerDto != null)
            {
                tamerDto.CurrentHp = tamer.CurrentHp;
                tamerDto.CurrentDs = tamer.CurrentDs;
                tamerDto.Digimons = _mapper.Map<List<DigimonDTO>>(tamer.Digimons);

                _context.Update(tamerDto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharactersStateAsync(CharacterStateEnum state)
        {
            var characters = await _context.Character
                .AsNoTracking()
                .ToListAsync();

            characters.ForEach(character =>
            {
                character.State = state;
                character.EventState = CharacterEventStateEnum.None;
            });

            _context.Character.UpdateRange(characters);
            _context.SaveChanges();
        }

        public async Task UpdateCharacterStateByIdAsync(long characterId, CharacterStateEnum state)
        {
            var character = await _context.Character
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == characterId);

            if (character != null)
            {
                character.State = state;

                _context.Character.Update(character);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterExperienceAsync(long tamerId, long currentExperience, byte level)
        {
            var dto = await _context.Character
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == tamerId);

            if (dto != null)
            {
                dto.CurrentExperience = currentExperience;
                dto.Level = level;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateDigimonExperienceAsync(DigimonModel digimon)
        {
            var dto = await _context.Digimon
                .AsNoTracking()
                .Include(x => x.Evolutions)
                .Include(x => x.AttributeExperience)
                .FirstOrDefaultAsync(x => x.Id == digimon.Id);

            if (dto != null)
            {
                dto.CurrentExperience = digimon.CurrentExperience;
                dto.CurrentSkillExperience = digimon.CurrentSkillExperience;
                dto.TranscendenceExperience = digimon.TranscendenceExperience;
                dto.Level = digimon.Level;

                dto.AttributeExperience.Data = digimon.AttributeExperience.Data;
                dto.AttributeExperience.Vaccine = digimon.AttributeExperience.Vaccine;
                dto.AttributeExperience.Virus = digimon.AttributeExperience.Virus;

                dto.AttributeExperience.Ice = digimon.AttributeExperience.Ice;
                dto.AttributeExperience.Water = digimon.AttributeExperience.Water;
                dto.AttributeExperience.Fire = digimon.AttributeExperience.Fire;
                dto.AttributeExperience.Land = digimon.AttributeExperience.Land;
                dto.AttributeExperience.Wind = digimon.AttributeExperience.Wind;
                dto.AttributeExperience.Wood = digimon.AttributeExperience.Wood;
                dto.AttributeExperience.Light = digimon.AttributeExperience.Light;
                dto.AttributeExperience.Dark = digimon.AttributeExperience.Dark;
                dto.AttributeExperience.Thunder = digimon.AttributeExperience.Thunder;
                dto.AttributeExperience.Steel = digimon.AttributeExperience.Steel;

                foreach (var evolutionDto in dto.Evolutions)
                {
                    var evolutionModel = digimon.Evolutions
                        .FirstOrDefault(x => x.Id == evolutionDto.Id);

                    if (evolutionModel != null)
                    {
                        evolutionDto.Type = evolutionModel.Type;
                        evolutionDto.Unlocked = evolutionModel.Unlocked;
                        evolutionDto.SkillPoints = evolutionModel.SkillPoints;
                        evolutionDto.SkillMastery = evolutionModel.SkillMastery;
                        evolutionDto.SkillExperience = evolutionModel.SkillExperience;
                    }
                }

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterSealsAsync(CharacterSealListModel sealList)
        {
            var dto = await _context.CharacterSealList
                .AsNoTracking()
                .Include(x => x.Seals)
                .FirstOrDefaultAsync(x => x.Id == sealList.Id);

            if (dto != null)
            {
                dto.SealLeaderId = sealList.SealLeaderId;

                foreach (var seal in sealList.Seals)
                {
                    var dtoSeal = dto.Seals.FirstOrDefault(x => x.Id == seal.Id);
                    if (dtoSeal != null)
                    {
                        dtoSeal.SealId = seal.SealId;
                        dtoSeal.SequentialId = seal.SequentialId;
                        dtoSeal.Favorite = seal.Favorite;
                        dtoSeal.Amount = seal.Amount;
                        _context.Update(dtoSeal);
                    }
                    else
                    {
                        dtoSeal = _mapper.Map<CharacterSealDTO>(seal);
                        dtoSeal.SealListId = sealList.Id;
                        dto.Seals.Add(dtoSeal);
                        _context.Add(dtoSeal);
                    }
                }

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task AddChatMessageAsync(ChatMessageModel chatMessage)
        {
            var dto = _mapper.Map<ChatMessageDTO>(chatMessage);
            if (dto != null)
            {
                _context.Add(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task AddCommandMessageAsync(CommandMessageModel commandMessage)
        {
            var dto = _mapper.Map<CommandMessageDTO>(commandMessage);

            if (dto != null)
            {
                _context.Add(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdatePartnerCurrentTypeAsync(DigimonModel digimon)
        {
            var dto = await _context.Digimon
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == digimon.Id);

            if (dto != null)
            {
                dto.CurrentType = digimon.CurrentType;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateDigicloneAsync(DigimonDigicloneModel digiclone)
        {
            var dto = await _context.DigimonDigiclone
                .AsNoTracking()
                .Include(x => x.History)
                .FirstOrDefaultAsync(x => x.Id == digiclone.Id || x.DigimonId == digiclone.DigimonId);

            if (dto != null)
            {
                dto.ATLevel = digiclone.ATLevel;
                dto.BLLevel = digiclone.BLLevel;
                dto.CTLevel = digiclone.CTLevel;
                dto.EVLevel = digiclone.EVLevel;
                dto.HPLevel = digiclone.HPLevel;

                dto.ATValue = digiclone.ATValue;
                dto.BLValue = digiclone.BLValue;
                dto.CTValue = digiclone.CTValue;
                dto.EVValue = digiclone.EVValue;
                dto.HPValue = digiclone.HPValue;

                dto.History.ATValues = digiclone.History.ATValues;
                dto.History.BLValues = digiclone.History.BLValues;
                dto.History.CTValues = digiclone.History.CTValues;
                dto.History.EVValues = digiclone.History.EVValues;
                dto.History.HPValues = digiclone.History.HPValues;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterTitleByIdAsync(long characterId, short titleId)
        {
            var dto = await _context.Character
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto != null)
            {
                dto.CurrentTitle = titleId;

                _context.Update(dto);

                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterProgressCompleteAsync(CharacterProgressModel progress)
        {
            var dto = await _context.CharacterProgress
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == progress.Id);

            if (dto != null)
            {
                dto.CompletedData = progress.CompletedData;
                dto.CompletedDataValue = progress.CompletedDataValue;

                _context.Update(dto);

                _context.SaveChanges();
            }
        }


        public async Task UpdateCharacterBuffListAsync(CharacterBuffListModel buffList)
        {
            var dto = await _context.CharacterBuffList
                .AsNoTracking()
                .Include(x => x.Buffs)
                .FirstOrDefaultAsync(x => x.Id == buffList.Id);

            if (dto != null)
            {
                // Remove os buffs em dto que não existem em buffList
                var buffsToRemove = dto.Buffs.Where(dtoBuff => !buffList.Buffs.Any(buff => buff.Id == dtoBuff.Id)).ToList();

                foreach (var buffToRemove in buffsToRemove)
                {
                    dto.Buffs.Remove(buffToRemove);
                    _context.Remove(buffToRemove);
                }

                foreach (var buff in buffList.Buffs.Where(x => !x.Expired))
                {
                    var dtoBuff = dto.Buffs.FirstOrDefault(x => x.Id == buff.Id);

                    if (dtoBuff != null)
                    {
                        dtoBuff.Duration = buff.Duration;
                        dtoBuff.EndDate = buff.EndDate;
                        dtoBuff.SkillId = buff.SkillId;
                        dtoBuff.TypeN = buff.TypeN;

                        _context.Update(dtoBuff);
                    }
                    else
                    {
                        dtoBuff = _mapper.Map<CharacterBuffDTO>(buff);
                        dtoBuff.BuffListId = buffList.Id;
                        dto.Buffs.Add(dtoBuff);

                        _context.Add(dtoBuff);
                    }
                }

                _context.SaveChanges();
            }
        }

        public async Task UpdateDigimonBuffListAsync(DigimonBuffListModel buffList)
        {
            var dto = await _context.DigimonBuffList
                .AsNoTracking()
                .Include(x => x.Buffs)
                .FirstOrDefaultAsync(x => x.Id == buffList.Id);

            if (dto != null)
            {
                // Remove os buffs em dto que não existem em buffList
                var buffsToRemove = dto.Buffs.Where(dtoBuff => !buffList.Buffs.Any(buff => buff.Id == dtoBuff.Id))
                    .ToList();
                foreach (var buffToRemove in buffsToRemove)
                {
                    dto.Buffs.Remove(buffToRemove);
                    _context.Remove(buffToRemove);
                    _context.SaveChanges();
                }


                // Atualiza ou adiciona os buffs em dto com base em buffList
                foreach (var buff in buffList.Buffs)
                {
                    var dtoBuff = dto.Buffs.FirstOrDefault(dtoBuff => dtoBuff.Id == buff.Id);
                    if (dtoBuff != null)
                    {
                        dtoBuff.Duration = buff.Duration;
                        dtoBuff.EndDate = buff.EndDate;
                        dtoBuff.SkillId = buff.SkillId;
                        dtoBuff.TypeN = buff.TypeN;
                        dtoBuff.CoolEndDate = buff.CoolEndDate;
                        dtoBuff.Cooldown = buff.Cooldown;
                        _context.Update(dtoBuff);
                        _context.SaveChanges();
                    }
                    else
                    {
                        dtoBuff = _mapper.Map<DigimonBuffDTO>(buff);
                        dtoBuff.BuffListId = buffList.Id;
                        dto.Buffs.Add(dtoBuff);
                        _context.Add(dtoBuff);
                        _context.SaveChanges();
                    }
                }
            }
        }

        public async Task UpdateCharacterActiveEvolutionAsync(CharacterActiveEvolutionModel activeEvolution)
        {
            var dto = await _context.CharacterActiveEvolution
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == activeEvolution.Id);

            if (dto != null)
            {
                dto.XgPerSecond = activeEvolution.XgPerSecond;
                dto.DsPerSecond = activeEvolution.DsPerSecond;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterBasicInfoAsync(CharacterModel character)
        {
            var dto = await _context.Character
                .AsNoTracking()
                .Include(x => x.Digimons)
                .SingleOrDefaultAsync(x => x.Id == character.Id);

            if (dto != null)
            {
                dto.CurrentHp = character.CurrentHp;
                dto.CurrentDs = character.CurrentDs;
                dto.XGauge = character.XGauge;
                dto.XCrystals = character.XCrystals;

                foreach (var digimonDto in dto.Digimons)
                {
                    var digimonModel = character.Digimons
                        .FirstOrDefault(x => x.Id == digimonDto.Id);

                    if (digimonModel != null)
                    {
                        digimonDto.CurrentHp = digimonModel.CurrentHp;
                        digimonDto.CurrentDs = digimonModel.CurrentDs;
                        digimonDto.CurrentType = digimonModel.CurrentType;
                    }
                }

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateItemListBitsAsync(long itemListId, long bits)
        {
            var dto = await _context.ItemLists
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == itemListId);

            if (dto != null)
            {
                dto.Bits = bits;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        // -------------------------------------------------------------------------------------

        public async Task UpdateItemsAsync(List<ItemModel> items)
        {
            await RemoveDeletedItems(items);
            await AddOrUpdateItems(items);

            _context.SaveChanges();
        }

        public async Task NewUpdateItemsAsync(List<ItemModel> items)
        {
            await RemoveDeletedItems2(items);
            await AddOrUpdateItems(items);

            _context.SaveChanges();
        }

        //private async Task AddOrUpdateItems(List<ItemModel> items)
        //{
        //    if (!items.Any())
        //        return;

        //    var itemsCopy = items.ToList();

        //    foreach (var item in itemsCopy)
        //    {
        //        var dto = await _context.Items
        //            .AsNoTracking()
        //            .Include(x => x.AccessoryStatus)
        //            .Include(x => x.SocketStatus)
        //            .FirstOrDefaultAsync(x => x.Id == item.Id);

        //        if (dto != null)
        //        {
        //            dto.Slot = item.Slot;
        //            dto.Amount = item.Amount;
        //            dto.ItemId = item.ItemId;
        //            dto.Duration = item.Duration;
        //            dto.EndDate = item.EndDate;
        //            dto.FirstExpired = item.FirstExpired;

        //            if (item.ItemListId > 0)
        //                dto.ItemListId = item.ItemListId;

        //            dto.RerollLeft = item.RerollLeft;
        //            dto.FamilyType = item.FamilyType;
        //            dto.Power = item.Power;

        //            dto.TamerShopSellPrice = item.TamerShopSellPrice;

        //            foreach (var dtoStatus in dto.AccessoryStatus)
        //            {
        //                var modelStatus = item.AccessoryStatus.First(x => x.Slot == dtoStatus.Slot);
        //                dtoStatus.Type = modelStatus.Type;
        //                dtoStatus.Value = modelStatus.Value;
        //            }

        //            foreach (var dtoStatus in dto.SocketStatus)
        //            {
        //                var modelStatus = item.SocketStatus.First(x => x.Slot == dtoStatus.Slot);
        //                dtoStatus.Type = modelStatus.Type;
        //                dtoStatus.AttributeId = modelStatus.AttributeId;
        //                dtoStatus.Value = modelStatus.Value;
        //            }


        //            _context.Update(dto);
        //        }
        //        else
        //        {
        //            _context.Add(_mapper.Map<ItemDTO>(item));
        //        }
        //    }
        //}

        private async Task AddOrUpdateItems(List<ItemModel> items)
        {
            if (!items.Any())
                return;

            var itemIdsToProcess = items.Select(y => y.Id).ToList();

            var existingItemIds = await _context.Items
                .AsNoTracking()
                .Where(x => itemIdsToProcess.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync();

            var itemsToAdd = new List<ItemModel>();
            var itemsToUpdate = new List<ItemModel>();

            foreach (var item in items)
            {
                if (existingItemIds.Contains(item.Id))
                {
                    itemsToUpdate.Add(item);
                }
                else
                {
                    itemsToAdd.Add(item);
                }
            }

            if (itemsToAdd.Any())
            {
                var itemsToAddDTO = _mapper.Map<List<ItemDTO>>(itemsToAdd);

                foreach (var itemDto in itemsToAddDTO)
                {
                    if (itemDto.AccessoryStatus != null)
                    {
                        foreach (var accessoryStatus in itemDto.AccessoryStatus)
                        {
                            accessoryStatus.Id = Guid.NewGuid();
                            accessoryStatus.ItemId = itemDto.Id;
                        }
                    }
                    if (itemDto.SocketStatus != null)
                    {
                        foreach (var socketStatus in itemDto.SocketStatus)
                        {
                            socketStatus.Id = Guid.NewGuid();
                            socketStatus.ItemId = itemDto.Id;
                        }
                    }
                }

                await _context.AddRangeAsync(itemsToAddDTO);
            }

            foreach (var item in itemsToUpdate)
            {
                var dto = await _context.Items
                    .Include(x => x.AccessoryStatus)
                    .Include(x => x.SocketStatus)
                    .FirstOrDefaultAsync(x => x.Id == item.Id);

                if (dto != null)
                {
                    dto.Slot = item.Slot;
                    dto.Amount = item.Amount;
                    dto.ItemId = item.ItemId;
                    dto.Duration = item.Duration;
                    dto.EndDate = item.EndDate;
                    dto.FirstExpired = item.FirstExpired;

                    if (item.ItemId > 0 && item.ItemListId > 0)
                    {
                        dto.ItemListId = item.ItemListId;
                    }

                    dto.RerollLeft = item.RerollLeft;
                    dto.FamilyType = item.FamilyType;
                    dto.Power = item.Power;
                    dto.TamerShopSellPrice = item.TamerShopSellPrice;

                    // Update AccessoryStatus (assuming always 8)
                    if (item.AccessoryStatus != null && dto.AccessoryStatus != null && item.AccessoryStatus.Count == 8 && dto.AccessoryStatus.Count == 8)
                    {
                        foreach (var modelStatus in item.AccessoryStatus)
                        {
                            var dtoStatus = dto.AccessoryStatus.FirstOrDefault(x => x.Slot == modelStatus.Slot);
                            if (dtoStatus != null)
                            {
                                dtoStatus.Type = modelStatus.Type;
                                dtoStatus.Value = modelStatus.Value;
                            }
                        }
                    }

                    // Update SocketStatus (assuming always 3)
                    if (item.SocketStatus != null && dto.SocketStatus != null && item.SocketStatus.Count == 3 && dto.SocketStatus.Count == 3)
                    {
                        foreach (var modelStatus in item.SocketStatus)
                        {
                            var dtoStatus = dto.SocketStatus.FirstOrDefault(x => x.Slot == modelStatus.Slot);
                            if (dtoStatus != null)
                            {
                                dtoStatus.Type = modelStatus.Type;
                                dtoStatus.AttributeId = modelStatus.AttributeId;
                                dtoStatus.Value = modelStatus.Value;
                            }
                        }
                    }

                    _context.Update(dto);
                }
            }

            await _context.SaveChangesAsync();
        }

        // Old Code delete each item separated
        //private async Task RemoveDeletedItems(List<ItemModel> items)
        //{
        //    if (!items.Any())
        //        return;

        //    var dtoItemsId = await _context.Items
        //        .AsNoTracking()
        //        .Where(x => x.ItemListId == items.First().ItemListId)
        //        .ToListAsync();

        //    var itemsToRemove = dtoItemsId
        //        .Where(x => !items.Select(y => y.Id).Contains(x.Id))
        //        .ToList();

        //    itemsToRemove.ForEach(itemToRemove => { _context.Remove(itemToRemove); });
        //}

        // Remove Slot when deleted

        private async Task RemoveDeletedItems2(List<ItemModel> items)
        {
            if (!items.Any())
                return;

            var itemListId = items.First().ItemListId;
            var existingItemIdsInModel = items
                .Where(item => item.ItemId != 0)
                .Select(y => y.Id)
                .ToList();

            await _context.Items
                .Where(x => x.ItemListId == itemListId && x.ItemId != 0 && !existingItemIdsInModel.Contains(x.Id))
                .ExecuteDeleteAsync();
        }

        // Dont remove slot when deleted
        private async Task RemoveDeletedItems(List<ItemModel> items)
        {
            if (!items.Any())
                return;

            var itemListId = items.First().ItemListId;
            var itemIdsToKeep = items
                .Where(item => item.ItemId != 0)
                .Select(y => y.Id)
                .ToList();

            var itemsToRemoveFromModel = await _context.Items
                .Where(x => x.ItemListId == itemListId && x.ItemId != 0 && !itemIdsToKeep.Contains(x.Id))
                .ToListAsync();

            foreach (var itemToRemove in itemsToRemoveFromModel)
            {
                itemToRemove.ItemId = 0;
                itemToRemove.Amount = 0;
                itemToRemove.Duration = 0;
                itemToRemove.EndDate = DateTime.Now;
                itemToRemove.FirstExpired = true;
                itemToRemove.TamerShopSellPrice = 0;

                _context.Update(itemToRemove);
            }
        }

        // -------------------------------------------------------------------------------------

        public async Task UpdateItemAccessoryStatusAsync(ItemModel item)
        {
            var dto = await _context.Items
                .AsNoTracking()
                .Include(x => x.AccessoryStatus)
                .FirstOrDefaultAsync(x => x.Id == item.Id);

            if (dto != null)
            {
                dto.RerollLeft = item.RerollLeft;
                dto.Power = item.Power;

                foreach (var dtoStatus in dto.AccessoryStatus)
                {
                    var modelStatus = item.AccessoryStatus.First(x => x.Slot == dtoStatus.Slot);
                    dtoStatus.Type = modelStatus.Type;
                    dtoStatus.Value = modelStatus.Value;
                }

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateItemSocketStatusAsync(ItemModel item)
        {
            var dto = await _context.Items
                .AsNoTracking()
                .Include(x => x.SocketStatus)
                .FirstOrDefaultAsync(x => x.Id == item.Id);

            if (dto != null)
            {
                dto.RerollLeft = item.RerollLeft;
                dto.Power = item.Power;

                foreach (var dtoStatus in dto.SocketStatus)
                {
                    var modelStatus = item.SocketStatus.First(x => x.Slot == dtoStatus.Slot);
                    dtoStatus.Type = modelStatus.Type;
                    dtoStatus.AttributeId = modelStatus.AttributeId;
                    dtoStatus.Value = modelStatus.Value;
                }

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateItemAsync(ItemModel item)
        {
            var dto = await _context.Items
                .AsNoTracking()
                .Include(x => x.AccessoryStatus)
                .Include(x => x.SocketStatus)
                .FirstOrDefaultAsync(x => x.Id == item.Id);

            if (dto != null)
            {
                dto.Amount = item.Amount;
                dto.ItemId = item.ItemId;
                dto.Duration = item.Duration;
                dto.EndDate = item.EndDate;
                dto.FirstExpired = item.FirstExpired;

                if (item.ItemListId > 0)
                    dto.ItemListId = item.ItemListId;

                dto.RerollLeft = item.RerollLeft;
                dto.Power = item.Power;

                dto.TamerShopSellPrice = item.TamerShopSellPrice;

                foreach (var dtoStatus in dto.AccessoryStatus)
                {
                    var modelStatus = item.AccessoryStatus.First(x => x.Slot == dtoStatus.Slot);
                    dtoStatus.Type = modelStatus.Type;
                    dtoStatus.Value = modelStatus.Value;
                }

                foreach (var dtoStatus in dto.SocketStatus)
                {
                    var modelStatus = item.SocketStatus.First(x => x.Slot == dtoStatus.Slot);
                    dtoStatus.Type = modelStatus.Type;
                    dtoStatus.AttributeId = modelStatus.AttributeId;
                    dtoStatus.Value = modelStatus.Value;
                }


                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateItemListSizeAsync(long itemListId, byte newSize)
        {
            var dto = await _context.ItemLists.FirstOrDefaultAsync(x => x.Id == itemListId);

            if (dto != null)
            {
                dto.Size = newSize;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task AddInventorySlotsAsync(List<ItemModel> items)
        {
            var itemListDto = await _context.ItemLists
                .AsNoTracking()
                .Include(x => x.Items)
                .ThenInclude(y => y.AccessoryStatus)
                .Include(x => x.Items)
                .ThenInclude(y => y.SocketStatus)
                .FirstOrDefaultAsync(x => x.Id == items.First().ItemListId);

            if (itemListDto != null)
            {
                foreach (var item in items)
                {
                    _context.Add(_mapper.Map<ItemDTO>(item));
                    itemListDto.Size += 1;
                }

                _context.Update(itemListDto);
            }

            _context.SaveChanges();
        }

        public async Task UpdateCharacterEventStateByIdAsync(long characterId, CharacterEventStateEnum state)
        {
            var dto = await _context.Character.FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto != null)
            {
                dto.EventState = state;

                _context.Update(dto);

                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateEvolutionAsync(DigimonEvolutionModel evolution)
        {
            var dto = await _context.DigimonEvolution
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == evolution.Id);

            if (dto != null)
            {
                dto.Type = evolution.Type;
                dto.Unlocked = evolution.Unlocked;
                dto.SkillPoints = evolution.SkillPoints;
                dto.SkillMastery = evolution.SkillMastery;
                dto.SkillExperience = evolution.SkillExperience;

                dto.Skills = _mapper.Map<List<DigimonEvolutionSkillDTO>>(evolution.Skills);

                _context.Update(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateIncubatorAsync(CharacterIncubatorModel incubator)
        {
            var dto = await _context.CharacterIncubator
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == incubator.Id);

            if (dto != null)
            {
                dto.EggId = incubator.EggId;
                dto.HatchLevel = incubator.HatchLevel;
                dto.BackupDiskId = incubator.BackupDiskId;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterMapRegionAsync(CharacterMapRegionModel mapRegion)
        {
            var dto = await _context.CharacterMapRegion
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == mapRegion.Id);

            if (dto != null)
            {
                dto.Unlocked = mapRegion.Unlocked;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateDigimonSizeAsync(long digimonId, short size)
        {
            var dto = await _context.Digimon
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == digimonId);

            if (dto != null)
            {
                dto.Size = size;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterInitialPacketSentOnceSentAsync(long characterId, bool sendOnceSent)
        {
            var dto = await _context.Character
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto != null)
            {
                dto.InitialPacketSentOnceSent = sendOnceSent;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterSizeAsync(long characterId, short size)
        {
            var dto = await _context.Character
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto != null)
            {
                dto.Size = size;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateDigimonGradeAsync(long digimonId, DigimonHatchGradeEnum grade)
        {
            var dto = await _context.Digimon
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == digimonId);

            if (dto != null)
            {
                dto.HatchGrade = grade;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterDigimonsOrderAsync(CharacterModel character)
        {
            foreach (var digimon in character.Digimons)
            {
                var dto = await _context.Digimon
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == digimon.Id);

                if (dto != null)
                {
                    dto.Slot = digimon.Slot;

                    _context.Update(dto);
                }
            }

            _context.SaveChanges();
        }

        public async Task DeleteDigimonAsync(long digimonId)
        {
            var dto = await _context.Digimon
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == digimonId);

            if (dto != null)
            {
                _context.Remove(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterDigimonArchiveItemAsync(CharacterDigimonArchiveItemModel characterDigimonArchiveItem)
        {
            var dto = await _context.CharacterDigimonArchiveItem
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == characterDigimonArchiveItem.Id);

            if (dto != null)
            {
                dto.DigimonId = characterDigimonArchiveItem.DigimonId;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateDigimonSlotAsync(long digimonId, byte digimonSlot)
        {
            var dto = await _context.Digimon
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == digimonId);

            if (dto != null)
            {
                dto.Slot = digimonSlot;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterXaiAsync(CharacterXaiModel xai)
        {
            var dto = await _context.CharacterXai
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == xai.Id);

            if (dto != null)
            {
                dto.ItemId = xai.ItemId;
                dto.XCrystals = xai.XCrystals;
                dto.XGauge = xai.XGauge;

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task AddDigimonArchiveSlotAsync(Guid archiveId, CharacterDigimonArchiveItemModel archiveItem)
        {
            var archiveDto = await _context.CharacterDigimonArchive
                .AsNoTracking()
                .Include(x => x.DigimonArchives)
                .SingleOrDefaultAsync(x => x.Id == archiveId);

            if (archiveDto != null)
            {
                var dto = _mapper.Map<CharacterDigimonArchiveItemDTO>(archiveItem);
                dto.DigimonArchiveId = archiveId;
                _context.CharacterDigimonArchiveItem.Add(dto);
                archiveDto.Slots++;

                _context.Update(archiveDto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterDigimonSlotsAsync(long characterId, byte slots)
        {
            var characterDto = await _context.Character
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == characterId);

            if (characterDto != null)
            {
                characterDto.DigimonSlots = slots;

                _context.Update(characterDto);
                _context.SaveChanges();
            }
        }

        public async Task<CharacterDTO> ChangeCharacterNameAsync(long characterId, string NewCharacterName)
        {
            var dto = await _context.Character
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto != null)
            {
                dto.Name = NewCharacterName;

                _context.Update(dto);

                _context.SaveChanges();
            }

            return dto;
        }

        public async Task<CharacterDTO> ChangeCharacterIdTpAsync(long characterId, int TargetTamerIdTP)
        {
            var dto = await _context.Character
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto != null)
            {
                dto.TargetTamerIdTP = TargetTamerIdTP;

                _context.Update(dto);

                _context.SaveChanges();
            }

            return dto;
        }

        public async Task<DigimonDTO> ChangeDigimonNameAsync(long digimonId, string NewDigimonName)
        {
            var dto = await _context.Digimon.AsNoTracking().FirstOrDefaultAsync(x => x.Id == digimonId);

            if (dto != null)
            {
                dto.Name = NewDigimonName;

                _context.Update(dto);

                _context.SaveChanges();
            }

            return dto;
        }

        public async Task<CharacterDTO> ChangeTamerModelAsync(long characterId, CharacterModelEnum model)
        {
            var dto = await _context.Character
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto != null)
            {
                dto.Model = model;

                _context.Update(dto);

                _context.SaveChanges();
            }

            return dto;
        }

        public async Task UpdateTamerSkillCooldownAsync(CharacterTamerSkillModel activeSkill)
        {
            var dto = await _context.ActiveSkills
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == activeSkill.Id);

            if (dto != null)
            {
                dto.SkillId = activeSkill.SkillId;
                dto.Cooldown = activeSkill.Cooldown;
                dto.EndCooldown = activeSkill.EndCooldown;
                dto.Type = activeSkill.Type;
                dto.Duration = activeSkill.Duration;
                dto.EndDate = activeSkill.EndDate;
                _context.Update(dto);

                _context.SaveChanges();
            }
        }

        public async Task AddInventorySlotAsync(ItemModel newSlot)
        {
            var itemListDto = await _context.ItemLists
                .AsNoTracking()
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == newSlot.ItemListId);

            if (itemListDto != null)
            {
                var dto = _mapper.Map<ItemDTO>(newSlot);
                _context.Add(dto);

                //itemListDto.Items.Add(dto);
                itemListDto.Size += 1;
                _context.Update(itemListDto);
            }

            _context.SaveChanges();
        }

        public async Task UpdateCharacterArenaPointsAsync(CharacterArenaPointsModel points)
        {
            var dto = await _context.CharacterPoints
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == points.Id);

            if (dto != null)
            {
                dto.CurrentStage = points.CurrentStage;
                dto.Amount = points.Amount;
                dto.ItemId = points.ItemId;

                _context.CharacterPoints.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task UpdateCharacterInProgressAsync(InProgressQuestModel progress)
        {
            var dto = await _context.InProgressQuest.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == progress.Id);

            if (dto != null)
            {
                dto.FirstCondition = progress.FirstCondition;
                dto.SecondCondition = progress.SecondCondition;
                dto.ThirdCondition = progress.ThirdCondition;
                dto.FourthCondition = progress.FourthCondition;
                dto.FifthCondition = progress.FifthCondition;

                _context.InProgressQuest.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task AddCharacterProgressAsync(CharacterProgressModel progress)
        {
            var dto = await _context.CharacterProgress
                .Include(x => x.InProgressQuestData)
                .FirstOrDefaultAsync(x => x.Id == progress.Id);

            if (dto != null)
            {
                var questsToAdd = progress.InProgressQuestData
                    .Where(quest => dto.InProgressQuestData.All(q => q.Id != quest.Id))
                    .ToList();

                foreach (var newQuest in questsToAdd)
                {
                    var questDto = _mapper.Map<InProgressQuestDTO>(newQuest);
                    questDto.CharacterProgressId = progress.Id;

                    _context.InProgressQuest.Add(questDto);
                    _context.SaveChanges();
                }
            }
        }

        public async Task UpdateTamerAttendanceRewardAsync(AttendanceRewardModel attendanceRewardModel)
        {
            var dto = await _context.AttendanceReward.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CharacterId == attendanceRewardModel.CharacterId);

            if (dto != null)
            {
                dto.LastRewardDate = attendanceRewardModel.LastRewardDate;
                dto.TotalDays = attendanceRewardModel.TotalDays;
                dto.RewardClaimedToday = attendanceRewardModel.RewardClaimedToday;

                _context.AttendanceReward.Update(dto);
                _context.SaveChanges();
            }

            return;
        }

        public async Task UpdateCharacterDeckBuffAsync(CharacterModel character)
        {
            var dto = await _context.Character
                .AsNoTracking()
                .Include(x => x.DeckBuff)
                .ThenInclude(x => x.Options)
                .ThenInclude(x => x.DeckBookInfo)
                .FirstOrDefaultAsync(x => x.Id == character.Id);


            if (dto != null)
            {
                DeckBuffAssetDTO? deckBuffModel = _context.DeckBuff.AsNoTracking()
                    .Include(x => x.Options)
                    .ThenInclude(x => x.DeckBookInfo)
                    .FirstOrDefault(x => x.Id == dto.DeckBuffId);

                dto.DeckBuffId = character.DeckBuffId;
                dto.DeckBuff = deckBuffModel;
                _context.Update(dto);
                _context.SaveChanges();
            }

            return;
        }

        public async Task UpdateTamerTimeRewardAsync(TimeRewardModel timeRewardModel)
        {
            var dto = await _context.TimeReward.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CharacterId == timeRewardModel.CharacterId);

            if (dto != null)
            {
                dto.StartTime = timeRewardModel.StartTime;
                dto.RewardIndex = timeRewardModel.RewardIndex;
                dto.AtualTime = timeRewardModel.AtualTime;

                _context.TimeReward.Update(dto);
                _context.SaveChanges();
            }

            return;
        }

        public async Task UpdateCharacterArenaDailyPointsAsync(CharacterArenaDailyPointsModel points)
        {
            var dto = await _context.CharacterDailyPoints
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == points.Id);

            if (dto != null)
            {
                dto.InsertDate = points.InsertDate;
                dto.Points = points.Points;
                _context.CharacterDailyPoints.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task<CharacterEncyclopediaModel> CreateCharacterEncyclopediaAsync(CharacterEncyclopediaModel characterEncyclopedia)
        {
            _logger.Information($"[CreateCharacterEncyclopediaAsync] CharacterId: {characterEncyclopedia.CharacterId}, DigimonEvolutionId: {characterEncyclopedia.DigimonEvolutionId}, Evolutions: {characterEncyclopedia.Evolutions?.Count ?? 0}");

            var tamerDto = await _context.Character
                .Include(x => x.Encyclopedia)
                .ThenInclude(x => x.Evolutions)
                .SingleOrDefaultAsync(x => x.Id == characterEncyclopedia.CharacterId);

            _logger.Information($"[CreateCharacterEncyclopediaAsync] tamerDto {(tamerDto == null ? "no" : $"CharacterId: {tamerDto.Id}")}");

            var dto = _mapper.Map<CharacterEncyclopediaDTO>(characterEncyclopedia);
            _logger.Information($"[CreateCharacterEncyclopediaAsync]  DTO  {dto.Evolutions?.Count ?? 0} ");

            var validEvolutions = new List<CharacterEncyclopediaEvolutionsDTO>();

            if (dto.Evolutions != null && dto.Evolutions.Any())
            {
                _logger.Information($"[CreateCharacterEncyclopediaAsync] Validando {dto.Evolutions.Count} ");
                foreach (var evolutionDto in dto.Evolutions)
                {
                    var digimonBaseInfoExists = await _context.DigimonBaseInfoAsset
                        .AnyAsync(dbi => dbi.Type == evolutionDto.DigimonBaseType);

                    if (digimonBaseInfoExists)
                    {
                        _logger.Information($"[CreateCharacterEncyclopediaAsync] DigimonBaseType {evolutionDto.DigimonBaseType} ");
                        validEvolutions.Add(evolutionDto);
                    }
                    else
                    {
                        _logger.Error($"[CreateCharacterEncyclopediaAsync] DigimonBaseType {evolutionDto.DigimonBaseType} ");
                    }
                }
                dto.Evolutions = validEvolutions;
                _logger.Information($"[CreateCharacterEncyclopediaAsync] {validEvolutions.Count} Evolution");
            }

            if (tamerDto != null)
            {
                try
                {
                    if (tamerDto.Encyclopedia == null)
                    {
                        _logger.Information($"[CreateCharacterEncyclopediaAsync] Enciclopedia no existe");
                        tamerDto.Encyclopedia = new List<CharacterEncyclopediaDTO>();
                    }

                    // Verificar por CharacterId y DigimonEvolutionId
                    var existingEncyclopedia = tamerDto.Encyclopedia.FirstOrDefault(e =>
                        e.CharacterId == characterEncyclopedia.CharacterId &&
                        e.DigimonEvolutionId == characterEncyclopedia.DigimonEvolutionId);

                    _logger.Information($"[CreateCharacterEncyclopediaAsync] existingEncyclopedia {(existingEncyclopedia == null ? "no" : $" para CharacterId: {existingEncyclopedia.CharacterId}, DigimonEvolutionId: {existingEncyclopedia.DigimonEvolutionId}")}");

                    if (existingEncyclopedia == null)
                    {
                        _logger.Information($"[CreateCharacterEncyclopediaAsync] {validEvolutions.Count} ");
                        tamerDto.Encyclopedia.Add(dto);

                        _logger.Information($"[CreateCharacterEncyclopediaAsync] ");
                        _context.Update(tamerDto);

                        _logger.Information($"[CreateCharacterEncyclopediaAsync] ");
                        await _context.SaveChangesAsync();
                        _logger.Information($"[CreateCharacterEncyclopediaAsync] ");

                        return _mapper.Map<CharacterEncyclopediaModel>(dto);
                    }
                    else
                    {
                        _logger.Information($"[CreateCharacterEncyclopediaAsync]  {existingEncyclopedia.Evolutions?.Count ?? 0} evoluciones");
                        return _mapper.Map<CharacterEncyclopediaModel>(existingEncyclopedia);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"[CreateCharacterEncyclopediaAsync] Error: {ex.Message}, StackTrace: {ex.StackTrace}");
                    return null;
                }
            }

            _logger.Warning($"[CreateCharacterEncyclopediaAsync] tamerDto");
            return _mapper.Map<CharacterEncyclopediaModel>(dto);
        }






        public async Task UpdateCharacterEncyclopediaAsync(CharacterEncyclopediaModel characterEncyclopedia)
        {
            _logger.Information($"[UpdateCharacterEncyclopediaAsync] Inicio para EncyclopediaId: {characterEncyclopedia.Id}, Evolutions: {characterEncyclopedia.Evolutions?.Count ?? 0}");

            var dto = await _context.CharacterEncyclopedia
                .AsNoTracking()
                .Include(x => x.Evolutions)
                .SingleOrDefaultAsync(x => x.Id == characterEncyclopedia.Id);

            _logger.Information($"[UpdateCharacterEncyclopediaAsync] DTO {(dto == null ? "no encontrado" : $"encontrado para Id: {dto.Id}, Evolutions existentes: {dto.Evolutions?.Count ?? 0}")}");

            try
            {
                _logger.Information($"[UpdateCharacterEncyclopediaAsync] Registrando propiedades de {characterEncyclopedia.Evolutions?.Count ?? 0} evoluciones");
                foreach (var evolution in characterEncyclopedia.Evolutions ?? new List<CharacterEncyclopediaEvolutionsModel>())
                {
                    foreach (PropertyInfo prop in evolution.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var propValue = prop.GetValue(evolution, null);
                        _logger.Information($"[UpdateCharacterEncyclopediaAsync] Evolution {evolution.DigimonBaseType}, Prop: {prop.Name}, Value: {propValue}");
                    }
                }

                var evolutions = _mapper.Map<List<CharacterEncyclopediaEvolutionsDTO>>(characterEncyclopedia.Evolutions);
                _logger.Information($"[UpdateCharacterEncyclopediaAsync] Mapeadas {evolutions?.Count ?? 0} evoluciones para actualizar");

                if (dto != null)
                {
                    _logger.Information($"[UpdateCharacterEncyclopediaAsync] Actualizando propiedades de la enciclopedia");
                    dto.Level = characterEncyclopedia.Level;
                    dto.Size = characterEncyclopedia.Size;
                    dto.EnchantAT = characterEncyclopedia.EnchantAT;
                    dto.EnchantBL = characterEncyclopedia.EnchantBL;
                    dto.EnchantCT = characterEncyclopedia.EnchantCT;
                    dto.EnchantEV = characterEncyclopedia.EnchantEV;
                    dto.EnchantHP = characterEncyclopedia.EnchantHP;
                    dto.IsRewardAllowed = characterEncyclopedia.IsRewardAllowed;
                    dto.IsRewardReceived = characterEncyclopedia.IsRewardReceived;
                    dto.CreateDate = DateTime.Now;
                    dto.Evolutions = evolutions;

                    _logger.Information($"[UpdateCharacterEncyclopediaAsync] Marcando DTO para actualización con {evolutions?.Count ?? 0} evoluciones");
                    _context.Update(dto);

                    _logger.Information($"[UpdateCharacterEncyclopediaAsync] Guardando cambios en la base de datos");
                    await _context.SaveChangesAsync();
                    _logger.Information($"[UpdateCharacterEncyclopediaAsync] Cambios guardados exitosamente para EncyclopediaId: {dto.Id}");
                }
                else
                {
                    _logger.Warning($"[UpdateCharacterEncyclopediaAsync] No se encontró enciclopedia para Id: {characterEncyclopedia.Id}, no se actualizó");
                }
            }
            catch (Exception e)
            {
                _logger.Error($"[UpdateCharacterEncyclopediaAsync] Error: {e.Message}, StackTrace: {e.StackTrace}");
                _logger.Error($"[UpdateCharacterEncyclopediaAsync] InnerException: {e.InnerException?.Message}");
                throw;
            }
        }

        public async Task UpdateCharacterEncyclopediaEvolutionsAsync(CharacterEncyclopediaEvolutionsModel characterEncyclopediaEvolution)
        {
            var dto = await _context.CharacterEncyclopediaEvolutions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == characterEncyclopediaEvolution.Id);

            try
            {
                if (dto != null)
                {
                    dto.IsUnlocked = characterEncyclopediaEvolution.IsUnlocked;
                    dto.CreateDate = DateTime.Now;

                    _context.CharacterEncyclopediaEvolutions.Update(dto);
                    _context.SaveChanges();
                }
            }
            catch (Exception e)
            {
                _logger.Error($"[UpdateCharacterEncyclopediaEvolutionsAsync] :: {e.Message}");
                _logger.Error($"{e.InnerException}");
                throw;
            }
        }

        public async Task UpdateCharacterFriendsAsync(CharacterModel? character, bool connected = false)
        {
            // Fetch and update records
            List<CharacterFriendDTO> dto;
            if (character != null)
            {
                dto = await _context.CharacterFriends
                    .Where(x => x.FriendId == character.Id)
                    .ToListAsync();
            }
            else
            {
                dto = await _context.CharacterFriends
                    .ToListAsync();
            }

            if (!dto.IsNullOrEmpty())
            {
                dto.ForEach(friend => friend.SetConnected(connected));
                await _context.SaveChangesAsync();
            }
        }
    }
}