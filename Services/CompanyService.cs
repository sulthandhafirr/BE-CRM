using CRM.Api.Data;
using CRM.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM.Api.Services
{
    public class CompanyService
    {
        private readonly AppDbContext _db;

        public CompanyService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<CompanySettingsResponse?> GetSettingsAsync(int companyId)
        {
            var company = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId);

            if (company is null) return null;

            return MapToResponse(company);
        }

        public async Task<CompanySettingsResponse?> UpdateSettingsAsync(int companyId, CompanySettingsDto dto)
        {
            var company = await _db.Companies
                .FirstOrDefaultAsync(c => c.Id == companyId);

            if (company is null) return null;

            // Only update fields that are provided
            if (dto.CompanyName is not null)
                company.CompanyName = dto.CompanyName;
            if (dto.SupportEmail is not null)
                company.SupportEmail = dto.SupportEmail;
            if (dto.PhoneNumber is not null)
                company.PhoneNumber = dto.PhoneNumber;
            if (dto.Timezone is not null)
                company.Timezone = dto.Timezone;
            if (dto.WorkingDays is not null)
                company.WorkingDays = dto.WorkingDays;
            if (dto.WorkingHoursStart is not null)
                company.WorkingHoursStart = dto.WorkingHoursStart;
            if (dto.WorkingHoursEnd is not null)
                company.WorkingHoursEnd = dto.WorkingHoursEnd;
            if (dto.TicketNumberFormat is not null)
                company.TicketNumberFormat = dto.TicketNumberFormat;
            if (dto.DateFormat is not null)
                company.DateFormat = dto.DateFormat;
            // Always set logoUrl (empty string = clear, URL = set)
            company.LogoUrl = dto.LogoUrl;

            await _db.SaveChangesAsync();

            return MapToResponse(company);
        }

        private static CompanySettingsResponse MapToResponse(Company company)
        {
            return new CompanySettingsResponse
            {
                Id = company.Id,
                CompanyName = company.CompanyName,
                CompanyCode = company.CompanyCode,
                SupportEmail = company.SupportEmail,
                PhoneNumber = company.PhoneNumber,
                Timezone = company.Timezone,
                WorkingDays = company.WorkingDays,
                WorkingHoursStart = company.WorkingHoursStart,
                WorkingHoursEnd = company.WorkingHoursEnd,
                TicketNumberFormat = company.TicketNumberFormat,
                DateFormat = company.DateFormat,
                LogoUrl = company.LogoUrl,
                CreatedAt = default, // not stored in response for now
            };
        }
    }
}
