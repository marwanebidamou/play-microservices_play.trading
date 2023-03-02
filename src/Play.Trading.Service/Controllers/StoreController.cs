using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Play.Common;
using Play.Trading.Service.Dtos;
using Play.Trading.Service.Entities;
using System.Security.Claims;

namespace Play.Trading.Service.Controllers
{
    [ApiController]
    [Route("store")]
    [Authorize]
    public class StoreController : ControllerBase
    {
        private readonly IRepository<CatalogItem> _catalogRepository;
        private readonly IRepository<ApplicationUser> _usersRepository;
        private readonly IRepository<InventoryItem> _inventoryRepository;

        public StoreController(IRepository<CatalogItem> catalogRepository, IRepository<ApplicationUser> usersRepository, IRepository<InventoryItem> inventoryRepository)
        {
            _catalogRepository = catalogRepository;
            _usersRepository = usersRepository;
            _inventoryRepository = inventoryRepository;
        }




        [HttpGet]
        public async Task<ActionResult<StoreDto>> GetAsync()
        {
            var userId = Guid.Parse(User.FindFirstValue("sub"));

            var catalogItems = await _catalogRepository.GetAllAsync();
            var inventoryItems = await _inventoryRepository.GetAllAsync(x => x.UserId == userId);
            var user = await _usersRepository.GetAsync(userId);

            var storeDto = new StoreDto
                (
                    Items: catalogItems.Select(catalogItem =>
                    new StoreItemDto
                    (
                        Id : catalogItem.Id,
                        Name : catalogItem.Name,
                        Description : catalogItem.Description,
                        Price : catalogItem.Price,
                        OwnedQuantity : inventoryItems.FirstOrDefault(inventoryItem =>
                            inventoryItem.CatalogItemId == catalogItem.Id)?.Quantity ?? 0
                    )),
                    UserGil: user.Gil
                );

            return Ok(storeDto);
        }
    }
}