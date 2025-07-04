using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.DTOs.Account;
using DigitalWorldOnline.Commons.DTOs.Character;
using Microsoft.EntityFrameworkCore;
using DigitalWorldOnline.Commons.Interfaces;
using System.Linq;
using DigitalWorldOnline.Commons.DTOs.Base;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Models.Account;
using AutoMapper;

namespace DigitalWorldOnline.Infrastructure.Repositories.Account
{
    public class AccountQueriesRepository : IAccountQueriesRepository
    {
        private readonly DatabaseContext _context;


        private readonly IMapper _mapper;



        public AccountQueriesRepository(DatabaseContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<AccountDTO?> GetAccountByUsernameAsync(string username)
        {
            return await _context.Account
                .AsNoTracking()
                .Include(x => x.SystemInformation)
                .Include(x => x.AccountBlock)
                .FirstOrDefaultAsync(x => x.Username == username);
        }

        public async Task<AccountDTO?> GetAccountByIdAsync(long id)
        {
            var dto = await _context.Account
                    .AsNoTracking()
                    .Include(x => x.SystemInformation)
                    .Include(x => x.AccountBlock)
                    .Include(x => x.ItemList)
                        .ThenInclude(y => y.Items)
                            .ThenInclude(z => z.AccessoryStatus) // Incluindo AccessoryStatus dentro de Items
                    .Include(x => x.ItemList)
                        .ThenInclude(y => y.Items)
                            .ThenInclude(z => z.SocketStatus) // Incluindo SocketStatus dentro de Items
                    .FirstOrDefaultAsync(x => x.Id == id);


            dto?.ItemList.ForEach(itemList => itemList.Items = itemList.Items.OrderBy(x => x.Slot).ToList());

            return dto;
        }

        public async Task<AccountBlockDTO?> GetAccountBlockByIdAsync(long id)
        {
            return await _context.AccountBlock
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<SystemInformationDTO?> GetSystemInformationByIdAsync(long id)
        {
            return await _context.SystemInformation
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<IList<AccountDTO>> GetAllAccountsAsync()
        {
            var accs = await _context.Account
                .AsNoTracking()
                .ToListAsync();

            accs.ForEach(acc =>
            {
                acc.Password = acc.Password.Base64Decrypt();
            });

            return accs;
        }

        public async Task<IList<CharacterDTO>> GetConnectedCharactersAsync()
        {
            return await _context.Character
                .AsNoTracking()
                .Where(x => x.State == CharacterStateEnum.Ready)
                .Include(x => x.Location)
                .Include(x => x.Digimons)
                .ToListAsync();
        }


        public async Task<AccountDTO> CreateGameAccountAsync(string username, string password, string email = null)
            {
                // 1️⃣ Usar AccountModel para consistencia
                var model = AccountModel.Create(
                    username: username,
                    password: password,
                    email: email ?? $"test-{Guid.NewGuid()}@test.com",
                    secondaryPassword: null,
                    accessLevel: AccountAccessLevelEnum.Default
                );

                // 2️⃣ Mapear a DTO con AutoMapper
                var dto = _mapper.Map<AccountDTO>(model);

                // 3️⃣ Guardar cuenta primero
                await _context.Account.AddAsync(dto);
                await _context.SaveChangesAsync();

                // 4️⃣ Insertar ItemLists explícitos
                var accountWideLists = new List<ItemListDTO>
        {
            new() { AccountId = dto.Id, Type = ItemListEnum.AccountWarehouse, Size = (byte)GeneralSizeEnum.InitialAccountWarehouse, Bits = 0 },
            new() { AccountId = dto.Id, Type = ItemListEnum.CashWarehouse, Size = (byte)GeneralSizeEnum.CashWarehouse, Bits = 0 },
            new() { AccountId = dto.Id, Type = ItemListEnum.ShopWarehouse, Size = (byte)GeneralSizeEnum.ShopWarehouse, Bits = 0 },
            new() { AccountId = dto.Id, Type = ItemListEnum.BuyHistory, Size = (byte)GeneralSizeEnum.CashShopBuyHistory, Bits = 0 }
        };

                _context.ItemLists.AddRange(accountWideLists);
                await _context.SaveChangesAsync();

                return dto;
            }

    }
}