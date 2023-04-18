using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MovieAPI.Model;
using MovieAPI.Services;

namespace MovieAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AgeRatingsController : ControllerBase
    {
        IAgeRatingService _ageRatingService;

        public AgeRatingsController(IAgeRatingService ageRatingService)
        {
            _ageRatingService = ageRatingService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _ageRatingService.GetAllAsync();
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _ageRatingService.GetByIdAsync(id);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPost]
        public async Task<IActionResult> Add(AgeRating ageRating)
        {
            var result = await _ageRatingService.AddAsync(ageRating);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpDelete]
        public async Task<IActionResult> Delete(AgeRating ageRating)
        {
            var result = await _ageRatingService.DeleteAsync(ageRating);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPut]
        public async Task<IActionResult> Update(AgeRating ageRating)
        {
            var result = await _ageRatingService.UpdateAsync(ageRating);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }
    }
}
