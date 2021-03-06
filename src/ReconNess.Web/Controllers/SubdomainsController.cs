using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReconNess.Core.Services;
using ReconNess.Entities;
using ReconNess.Web.Dtos;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ReconNess.Web.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SubdomainsController : ControllerBase
    {
        private readonly IMapper mapper;
        private readonly ISubdomainService subdomainService;
        private readonly ITargetService targetService;
        private readonly IRootDomainService rootDomainService;
        private readonly ILabelService labelService;

        /// <summary>
        /// Initializes a new instance of the <see cref="SubdomainsController" /> class
        /// </summary>
        /// <param name="mapper"><see cref="IMapper"/></param>
        /// <param name="subdomainService"><see cref="ISubdomainService"/></param>
        /// <param name="targetService"><see cref="ITargetService"/></param>
        /// <param name="rootDomainService"><see cref="IRootDomainService"/></param>
        /// <param name="labelService"><see cref="ILabelService"/></param>
        public SubdomainsController(
            IMapper mapper,
            ISubdomainService subdomainService,
            ITargetService targetService,
            IRootDomainService rootDomainService,
            ILabelService labelService)
        {
            this.mapper = mapper;
            this.subdomainService = subdomainService;
            this.targetService = targetService;
            this.rootDomainService = rootDomainService;
            this.labelService = labelService;
        }

        // GET api/subdomains/{target}/{rootDomain}/{subdomain}
        [HttpGet("{targetName}/{rootDomain}/{subdomainName}")]
        public async Task<IActionResult> Get(string targetName, string rootDomain, string subdomainName, CancellationToken cancellationToken)
        {
            var target = await this.targetService.GetByCriteriaAsync(t => t.Name == targetName, cancellationToken);
            if (target == null)
            {
                return BadRequest();
            }

            var domain = await this.rootDomainService.GetByCriteriaAsync(t => t.Name == rootDomain, cancellationToken);
            if (domain == null)
            {
                return BadRequest();
            }

            var subdomain = await this.subdomainService.GetAllQueryableByCriteria(s => s.Domain == domain && s.Name == subdomainName, cancellationToken)
                .Include(s => s.Notes)
                .Include(s => s.Services)
                .Include(s => s.ServiceHttp)
                    .ThenInclude(s => s.Directories)
                .Include(s => s.Labels)
                    .ThenInclude(s => s.Label)
                .FirstOrDefaultAsync(cancellationToken);

            if (subdomain == null)
            {
                return NotFound();
            }

            return Ok(this.mapper.Map<Subdomain, SubdomainDto>(subdomain));
        }

        // POST api/subdomains
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] SubdomainDto subdomainDto, CancellationToken cancellationToken)
        {
            var target = await this.targetService.GetByCriteriaAsync(t => t.Name == subdomainDto.Target, cancellationToken);
            if (target == null)
            {
                return BadRequest();
            }

            var domain = await this.rootDomainService.GetByCriteriaAsync(t => t.Name == subdomainDto.RootDomain && t.Target == target, cancellationToken);
            if (domain == null)
            {
                return BadRequest();
            }

            if (await this.subdomainService.AnyAsync(s => s.Name == subdomainDto.Name && s.Domain == domain))
            {
                return BadRequest($"The subdomain {subdomainDto.Name} exist");
            }

            var newSubdoamin = await this.subdomainService.AddAsync(new Subdomain
            {
                Domain = domain,
                Name = subdomainDto.Name
            }, cancellationToken);

            return Ok(mapper.Map<Subdomain, SubdomainDto>(newSubdoamin));
        }

        // POST api/subdomains/{targetName}/{rootDomain}
        [HttpPost("{targetName}/{rootDomain}")]
        public async Task<IActionResult> Upload(string targetName, string rootDomain, IFormFile file, CancellationToken cancellationToken)
        {
            if (file.Length == 0)
            {
                return BadRequest();
            }

            var target = await this.targetService.GetByCriteriaAsync(t => t.Name == targetName, cancellationToken);
            if (target == null)
            {
                return NotFound();
            }

            var domain = await this.rootDomainService.GetDomainWithSubdomainsAsync(t => t.Name == rootDomain && t.Target == target, cancellationToken);
            if (domain == null)
            {
                return NotFound();
            }

            try
            {
                var path = Path.GetTempFileName();
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var subdomains = System.IO.File.ReadAllLines(path).ToList();
                var subdomainsAdded = await this.rootDomainService.UploadSubdomainsAsync(domain, subdomains);

                return Ok(this.mapper.Map<List<Subdomain>, List<SubdomainDto>>(subdomainsAdded));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // PUT api/subdomains/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Put(Guid id, [FromBody] SubdomainDto subdomainDto, CancellationToken cancellationToken)
        {
            var subdomain = await this.subdomainService.GetAllQueryableByCriteria(a => a.Id == id, cancellationToken)
               .Include(a => a.Labels)
                   .ThenInclude(ac => ac.Label)
               .FirstOrDefaultAsync();

            if (subdomain == null)
            {
                return NotFound();
            }

            var editedSubdmain = this.mapper.Map<SubdomainDto, Subdomain>(subdomainDto);

            subdomain.Name = editedSubdmain.Name;
            subdomain.IsMainPortal = editedSubdmain.IsMainPortal;
            subdomain.Labels = await this.labelService.GetLabelsAsync(subdomain.Labels, subdomainDto.Labels.Select(l => l.Name).ToList(), cancellationToken);

            await this.subdomainService.UpdateAsync(subdomain, cancellationToken);

            return NoContent();
        }

        // PUT api/subdomains/label/{id}
        [HttpPut("label/{id}")]
        public async Task<IActionResult> AddLabel(Guid id, [FromBody] SubdomainLabelDto subdomainLabelDto, CancellationToken cancellationToken)
        {
            var subdomain = await this.subdomainService.GetAllQueryableByCriteria(a => a.Id == id, cancellationToken)
               .Include(a => a.Labels)
                   .ThenInclude(ac => ac.Label)
               .FirstOrDefaultAsync();

            if (subdomain == null)
            {
                return NotFound();
            }

            var myLabels = subdomain.Labels.Select(l => l.Label.Name).ToList();
            myLabels.Add(subdomainLabelDto.Label);

            subdomain.Labels = await this.labelService.GetLabelsAsync(subdomain.Labels, myLabels, cancellationToken);

            subdomain = await this.subdomainService.UpdateAsync(subdomain, cancellationToken);
            var newLabel = subdomain.Labels.First(l => l.Label.Name == subdomainLabelDto.Label).Label;

            return Ok(mapper.Map<Label, LabelDto>(newLabel));
        }

        // DELETE api/subdomains/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            var subdomain = await this.subdomainService.GetAllQueryableByCriteria(s => s.Id == id, cancellationToken)
                .Include(s => s.Notes)
                .Include(s => s.Services)
                .FirstOrDefaultAsync(cancellationToken);

            if (subdomain == null)
            {
                return NotFound();
            }

            await this.subdomainService.DeleteSubdomainAsync(subdomain, cancellationToken);

            return NoContent();
        }
    }
}
