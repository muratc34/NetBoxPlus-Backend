using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MovieAPI.Model;
using MovieAPI.Services;
using Nest;

namespace MovieAPI.Controllers
{
    
    [Route("api/[controller]")]
    [ApiController]
    
    public class MoviesController : ControllerBase
    {
        IMovieService _movieService;
        private readonly IElasticClient _elasticClient;

        public MoviesController(IMovieService movieService, IElasticClient elasticClient)
        {
            _movieService = movieService;
            _elasticClient = elasticClient;
        }

        [HttpGet("search")]
        public async Task<IActionResult> GetBySearch(string keyword)
        {
            var result = await _movieService.GetBySearch(keyword);
            if (result.Success)
            {
                return Ok(result);
            }

            return Ok(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _movieService.GetAllAsync();
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _movieService.GetByIdAsync(id);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> Add( [FromForm] MovieDto movieDto)
        {
            var result = await _movieService.AddAsync(movieDto);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpDelete]
        public async Task<IActionResult> Delete(Movie movie)
        {
            var result = await _movieService.DeleteAsync(movie);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPut]
        public async Task<IActionResult> Update(Movie movie)
        {
            var result = await _movieService.UpdateAsync(movie);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpGet("getbygenre")]
        public async Task<IActionResult> GetAllByGenreId(Guid genreId)
        {
            var result = await _movieService.GetByGenreIdAsync(genreId);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpGet("getbygenresimilarity")]
        public async Task<IActionResult> GetAllByGenreSimilarity([FromQuery] List<Guid> genreIds)
        {
            var result = await _movieService.GetByGenreSimilatiry(genreIds);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }
    }
}
