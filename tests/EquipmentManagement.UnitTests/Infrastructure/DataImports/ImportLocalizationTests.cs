using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Infrastructure.DataImports;

namespace EquipmentManagement.UnitTests.Infrastructure.DataImports;

/// <summary>
/// The pt-PT Excel contract (ADR-070): the canonical headers, the legacy
/// English aliases that stay accepted, the display values for enums, and the
/// wording of row messages — which must name a field by its workbook header,
/// never by its internal key.
/// </summary>
public class ImportLocalizationTests
{
    /// <summary>Internal names that must never reach a cell or a message a person reads.</summary>
    private static readonly string[] InternalNames =
    [
        "FullName", "Phone", "Address", "WorkforceType", "EngagementType", "RoleOrFunction", "Company", "AllocationStartDate",
        "AllocationEndDate", "EquipmentType", "Manufacturer", "SerialNumber", "Notes", "Processor", "Memory", "Storage",
        "Internal", "External", "ServiceProvider", "EmploymentContract",
    ];

    [Fact]
    public void ConsultantColumns_HaveTheCanonicalPortugueseHeaders_InTemplateOrder()
    {
        Assert.Equal(
            ["Nome completo", "NIF", "Email", "Telefone", "Morada", "Regime", "Tipo de vínculo", "Função", "Empresa / Cliente", "Data de início", "Data de fim"],
            ConsultantImportColumns.All.Select(c => c.Header));
    }

    [Fact]
    public void ConsultantColumns_KeepEveryLegacyEnglishHeader()
    {
        string[] legacyTemplate =
            ["FullName", "NIF", "Email", "Phone", "Address", "WorkforceType", "EngagementType", "RoleOrFunction", "Company", "AllocationStartDate", "AllocationEndDate"];

        // Column by column, in order: the legacy header of the same position still resolves to that column.
        Assert.Equal(legacyTemplate.Length, ConsultantImportColumns.All.Count);
        for (var i = 0; i < legacyTemplate.Length; i++)
        {
            var column = ConsultantImportColumns.All[i];
            Assert.Contains(legacyTemplate[i], column.AcceptedHeaders);
            Assert.Equal(legacyTemplate[i], column.Field);
        }
    }

    [Fact]
    public void ConsultantColumns_RequireOnlyTheProfileHeaders()
    {
        Assert.Equal(
            ["Nome completo", "NIF", "Email", "Telefone", "Morada"],
            ConsultantImportColumns.All.Where(c => c.IsHeaderRequired).Select(c => c.Header));
    }

    [Fact]
    public void EquipmentColumns_HaveTheCanonicalPortugueseHeaders_InTemplateOrder()
    {
        Assert.Equal(
            ["Tipo de equipamento", "Fabricante", "Modelo", "Número de série", "Observações", "Processador", "Memória RAM", "Armazenamento"],
            EquipmentImportColumns.All.Select(c => c.Header));
    }

    [Fact]
    public void EquipmentColumns_KeepEveryLegacyEnglishHeader_AndStillRequireEveryColumn()
    {
        string[] legacyTemplate = ["EquipmentType", "Manufacturer", "Model", "SerialNumber", "Notes", "Processor", "Memory", "Storage"];

        Assert.Equal(legacyTemplate.Length, EquipmentImportColumns.All.Count);
        for (var i = 0; i < legacyTemplate.Length; i++)
        {
            var column = EquipmentImportColumns.All[i];
            Assert.Contains(legacyTemplate[i], column.AcceptedHeaders);
            Assert.Equal(legacyTemplate[i], column.Field);
            Assert.True(column.IsHeaderRequired);
        }
    }

    [Fact]
    public void AcceptedHeaders_NeverCollideBetweenTwoColumnsOfTheSameImport()
    {
        foreach (var columns in new[] { ConsultantImportColumns.All, EquipmentImportColumns.All })
        {
            var normalized = columns.SelectMany(c => c.AcceptedHeaders.Select(ImportColumn.NormalizeHeader).Distinct()).ToList();
            Assert.Equal(normalized.Count, normalized.Distinct().Count());
        }
    }

    [Theory]
    [InlineData("  Número de série ", "NÚMERO DE SÉRIE")]
    [InlineData("número   de\tsérie", "NÚMERO DE SÉRIE")]
    [InlineData("Número de série", "NÚMERO DE SÉRIE")]
    [InlineData("Empresa / Cliente", "EMPRESA / CLIENTE")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeHeader_TrimsCollapsesWhitespaceAndIgnoresCase(string? header, string expected) =>
        Assert.Equal(expected, ImportColumn.NormalizeHeader(header));

    [Fact]
    public void NormalizeHeader_KeepsAccentsSignificant() =>
        Assert.NotEqual(ImportColumn.NormalizeHeader("Número de série"), ImportColumn.NormalizeHeader("Numero de serie"));

    [Theory]
    [InlineData("Interno", WorkforceType.Internal)]
    [InlineData("Externo", WorkforceType.External)]
    [InlineData("  externo ", WorkforceType.External)]
    [InlineData("EXTERNO", WorkforceType.External)]
    [InlineData("Internal", WorkforceType.Internal)] // legacy English value
    [InlineData("external", WorkforceType.External)] // legacy English value, case-insensitive as before
    public void WorkforceType_AcceptsThePortugueseLabelAndTheLegacyEnglishName(string text, WorkforceType expected)
    {
        Assert.True(ExcelDisplayValues.TryParseWorkforceType(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("Prestador de serviço", EngagementType.ServiceProvider)]
    [InlineData("Contrato de trabalho", EngagementType.EmploymentContract)]
    [InlineData("prestador  de serviço", EngagementType.ServiceProvider)]
    [InlineData("ServiceProvider", EngagementType.ServiceProvider)] // legacy English value
    [InlineData("employmentcontract", EngagementType.EmploymentContract)] // legacy English value
    public void EngagementType_AcceptsThePortugueseLabelAndTheLegacyEnglishName(string text, EngagementType expected)
    {
        Assert.True(ExcelDisplayValues.TryParseEngagementType(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("Banana")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1")] // a number is not a value, even though it is a valid enum ordinal
    [InlineData("7")]
    [InlineData("Prestador de servico")] // accents are significant
    [InlineData("Interna")]
    public void EnumValues_RejectAnythingElse(string? text)
    {
        Assert.False(ExcelDisplayValues.TryParseWorkforceType(text, out _));
        Assert.False(ExcelDisplayValues.TryParseEngagementType(text, out _));
    }

    [Fact]
    public void DisplayLabels_CoverEveryEnumMember_InPortuguese()
    {
        Assert.Equal(["Interno", "Externo"], Enum.GetValues<WorkforceType>().Select(ExcelDisplayValues.Label));
        Assert.Equal(["Prestador de serviço", "Contrato de trabalho"], Enum.GetValues<EngagementType>().Select(ExcelDisplayValues.Label));
        Assert.Equal(["Ativa", "Terminada"], Enum.GetValues<AllocationStatus>().Select(ExcelDisplayValues.Label));
        Assert.Equal(["Genérico", "Portátil", "Telemóvel"], Enum.GetValues<EquipmentTypeKind>().Select(ExcelDisplayValues.Label));
        Assert.Equal(
            ["Disponível", "Em trânsito", "Atribuído", "Devolução pendente", "Em inspeção", "Avariado", "Em manutenção", "Abatido"],
            Enum.GetValues<EquipmentStatus>().Select(ExcelDisplayValues.Label));
        Assert.Equal("Sim", ExcelDisplayValues.Label(true));
        Assert.Equal("Não", ExcelDisplayValues.Label(false));
    }

    [Fact]
    public void ConsultantRow_RequiredFieldErrors_NameTheWorkbookHeaders()
    {
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(ConsultantInput(fullName: null, nif: null, email: null, address: null, phone: null), issues);

        Assert.Equal("O campo \"Nome completo\" é obrigatório.", MessageFor(issues, ConsultantImportIssueCodes.MissingFullName));
        Assert.Equal("O campo \"Email\" é obrigatório.", MessageFor(issues, ConsultantImportIssueCodes.MissingEmail));
        Assert.Equal("O campo \"Morada\" é obrigatório.", MessageFor(issues, ConsultantImportIssueCodes.MissingAddress));
        Assert.Equal("O campo \"Telefone\" é obrigatório.", MessageFor(issues, ConsultantImportIssueCodes.MissingPhone));
        Assert.Equal(
            "O campo \"NIF\" é obrigatório e deve conter um número de identificação fiscal válido.",
            MessageFor(issues, ConsultantImportIssueCodes.InvalidNif));

        // The machine-readable field key is unchanged by the localization.
        Assert.Equal("FullName", issues.Single(i => i.Code == ConsultantImportIssueCodes.MissingFullName).Field);
        AssertNoInternalNames(issues);
    }

    [Fact]
    public void ConsultantRow_InvalidEnumValues_ListTheAcceptedPortugueseValues()
    {
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(
            ConsultantInput(workforceType: "Banana", engagementType: "Avença", roleOrFunction: "Consultora", startDate: new DateOnly(2026, 1, 15)), issues);

        Assert.Equal(
            "O valor \"Banana\" indicado em \"Regime\" não é válido. Valores aceites: Interno, Externo.",
            MessageFor(issues, ConsultantImportIssueCodes.InvalidWorkforceType));
        Assert.Equal(
            "O valor \"Avença\" indicado em \"Tipo de vínculo\" não é válido. Valores aceites: Prestador de serviço, Contrato de trabalho.",
            MessageFor(issues, ConsultantImportIssueCodes.InvalidEngagementType));
        AssertNoInternalNames(issues);
    }

    [Fact]
    public void ConsultantRow_AllocationBlockErrors_AreInPortuguese()
    {
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(ConsultantInput(companyName: "Cliente Exemplo", startDateInvalid: true, endDateInvalid: true), issues);

        Assert.Equal(
            "O campo \"Regime\" é obrigatório quando são indicados dados de alocação.", MessageFor(issues, ConsultantImportIssueCodes.MissingWorkforceType));
        Assert.Equal(
            "O campo \"Tipo de vínculo\" é obrigatório quando são indicados dados de alocação.",
            MessageFor(issues, ConsultantImportIssueCodes.MissingEngagementType));
        Assert.Equal(
            "O campo \"Função\" é obrigatório quando são indicados dados de alocação.", MessageFor(issues, ConsultantImportIssueCodes.MissingRoleOrFunction));
        Assert.Contains(
            issues, i => i.Field == "AllocationStartDate" && i.Message == "O valor indicado em \"Data de início\" não é uma data válida. Utilize o formato DD/MM/AAAA.");
        Assert.Contains(
            issues, i => i.Field == "AllocationEndDate" && i.Message == "O valor indicado em \"Data de fim\" não é uma data válida. Utilize o formato DD/MM/AAAA.");
        AssertNoInternalNames(issues);
    }

    [Fact]
    public void ConsultantRow_CompanyRuleAndDateOrderErrors_AreInPortuguese()
    {
        var external = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(
            ConsultantInput(workforceType: "Externo", engagementType: "Prestador de serviço", roleOrFunction: "Consultora",
                startDate: new DateOnly(2026, 3, 1), endDate: new DateOnly(2026, 2, 1)), external);
        Assert.Equal("O campo \"Empresa / Cliente\" é obrigatório quando o regime é Externo.", MessageFor(external, ConsultantImportIssueCodes.MissingCompany));
        Assert.Equal("A \"Data de fim\" não pode ser anterior à \"Data de início\".", MessageFor(external, ConsultantImportIssueCodes.InvalidAllocationDate));

        var @internal = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(
            ConsultantInput(workforceType: "Interno", engagementType: "Contrato de trabalho", roleOrFunction: "Consultora",
                companyName: "Cliente Exemplo", startDate: new DateOnly(2026, 3, 1)), @internal);
        Assert.Equal(
            "O campo \"Empresa / Cliente\" deve ficar vazio quando o regime é Interno.",
            MessageFor(@internal, ConsultantImportIssueCodes.CompanyNotAllowedForInternal));
    }

    [Fact]
    public void ConsultantRow_FormulaAndNumericIdentifierErrors_NameTheWorkbookHeaders()
    {
        var issues = new List<ImportIssue>();
        ConsultantImportRowValidator.ValidateStructural(
            ConsultantInput(nifAmbiguous: true, phoneAmbiguous: true, formulaFields: ["RoleOrFunction", "FullName"]), issues);

        Assert.Contains(issues, i => i.Field == "RoleOrFunction" && i.Message == "O campo \"Função\" contém uma fórmula. As fórmulas não são aceites.");
        Assert.Contains(issues, i => i.Field == "FullName" && i.Message == "O campo \"Nome completo\" contém uma fórmula. As fórmulas não são aceites.");
        Assert.Contains(issues, i => i.Field == "NIF" && i.Message.StartsWith("O valor de \"NIF\" foi guardado como número", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Field == "Phone" && i.Message.StartsWith("O valor de \"Telefone\" foi guardado como número", StringComparison.Ordinal));
        AssertNoInternalNames(issues);
    }

    [Fact]
    public void EquipmentRow_Errors_NameTheWorkbookHeaders()
    {
        var structural = new List<ImportIssue>();
        var fields = EquipmentImportRowValidator.ValidateStructural(
            new EquipmentImportRowInput(2, null, null, null, null, SerialNumberIsAmbiguousNumeric: true, null, null, null, null, new HashSet<string> { "Notes" }),
            structural);

        Assert.Equal("O campo \"Tipo de equipamento\" é obrigatório.", MessageFor(structural, EquipmentImportIssueCodes.MissingEquipmentType));
        Assert.Equal("O campo \"Observações\" contém uma fórmula. As fórmulas não são aceites.", MessageFor(structural, EquipmentImportIssueCodes.FormulaNotAllowed));
        Assert.StartsWith(
            "O valor de \"Número de série\" foi guardado como número", MessageFor(structural, EquipmentImportIssueCodes.NumericIdentifierRejected), StringComparison.Ordinal);
        AssertNoInternalNames(structural);

        var laptop = new List<ImportIssue>();
        EquipmentImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.Laptop, fields, laptop);
        Assert.Equal("O campo \"Fabricante\" é obrigatório para portáteis.", MessageFor(laptop, EquipmentImportIssueCodes.MissingLaptopManufacturer));
        Assert.Equal("O campo \"Número de série\" é obrigatório para portáteis.", MessageFor(laptop, EquipmentImportIssueCodes.MissingLaptopSerial));
        Assert.Equal("O campo \"Processador\" é obrigatório para portáteis.", MessageFor(laptop, EquipmentImportIssueCodes.MissingLaptopProcessor));
        Assert.Equal("O campo \"Memória RAM\" é obrigatório para portáteis.", MessageFor(laptop, EquipmentImportIssueCodes.MissingLaptopMemory));
        Assert.Equal("O campo \"Armazenamento\" é obrigatório para portáteis.", MessageFor(laptop, EquipmentImportIssueCodes.MissingLaptopStorage));
        Assert.Equal("SerialNumber", laptop.Single(i => i.Code == EquipmentImportIssueCodes.MissingLaptopSerial).Field);
        AssertNoInternalNames(laptop);

        var generic = new List<ImportIssue>();
        EquipmentImportRowValidator.ValidateAgainstKind(EquipmentTypeKind.Generic, fields with { Processor = "i5" }, generic);
        Assert.Equal(
            "Os campos \"Processador\", \"Memória RAM\" e \"Armazenamento\" só podem ser preenchidos para portáteis.",
            MessageFor(generic, EquipmentImportIssueCodes.LaptopFieldsNotAllowedForGeneric));
    }

    private static string MessageFor(List<ImportIssue> issues, string code) => issues.First(i => i.Code == code).Message;

    private static void AssertNoInternalNames(IEnumerable<ImportIssue> issues)
    {
        foreach (var issue in issues)
        {
            foreach (var name in InternalNames)
            {
                Assert.DoesNotContain(name, issue.Message, StringComparison.Ordinal);
            }
        }
    }

    private static ConsultantImportRowInput ConsultantInput(
        string? fullName = "Maria Exemplo", string? nif = "123456789", string? email = "maria@example.invalid", string? address = "Rua Exemplo 1",
        string? phone = "+351900000000", bool nifAmbiguous = false, bool phoneAmbiguous = false, string? workforceType = null,
        string? engagementType = null, string? roleOrFunction = null, string? companyName = null, DateOnly? startDate = null,
        bool startDateInvalid = false, DateOnly? endDate = null, bool endDateInvalid = false, string[]? formulaFields = null) => new(
        RowNumber: 2,
        FullName: fullName,
        Nif: nifAmbiguous ? null : nif,
        NifIsAmbiguousNumeric: nifAmbiguous,
        Email: email,
        Address: address,
        Phone: phoneAmbiguous ? null : phone,
        PhoneIsAmbiguousNumeric: phoneAmbiguous,
        WorkforceType: workforceType,
        EngagementType: engagementType,
        RoleOrFunction: roleOrFunction,
        CompanyName: companyName,
        AllocationStartDate: startDate,
        AllocationStartDateInvalid: startDateInvalid,
        AllocationEndDate: endDate,
        AllocationEndDateInvalid: endDateInvalid,
        FormulaFields: new HashSet<string>(formulaFields ?? []));
}
