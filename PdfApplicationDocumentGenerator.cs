using System;
using System.Linq;
using Nml.Improve.Me.Dependencies;

namespace Nml.Improve.Me
{
	public class PdfApplicationDocumentGenerator : IApplicationDocumentGenerator
	{
		readonly IDataContext _dataContext;
		readonly IPathProvider _templatePathProvider;
		readonly IViewGenerator _viewGenerator;
		readonly IConfiguration _configuration;
		readonly ILogger<PdfApplicationDocumentGenerator> _logger;
		readonly IPdfGenerator _pdfGenerator;

		public PdfApplicationDocumentGenerator(
			IDataContext dataContext,
			IPathProvider templatePathProvider,
			IViewGenerator viewGenerator,
			IConfiguration configuration,
			IPdfGenerator pdfGenerator,
			ILogger<PdfApplicationDocumentGenerator> logger)
		{
			_dataContext = dataContext;
			_templatePathProvider = templatePathProvider;
			_viewGenerator = viewGenerator;
			_configuration = configuration;
			_logger = logger;
			_pdfGenerator = pdfGenerator;
		}

		public byte[] Generate(Guid applicationId, string baseUri)
		{
			Application application = _dataContext.Applications.Single(app => app.Id == applicationId);

			if (application != null)
			{

				if (baseUri.EndsWith("/"))
					baseUri = baseUri[^1..];

				string view = string.Empty;

                if (application.State != ApplicationState.Closed)                
					view = GenerateView(application, baseUri);                
				else
				{
					_logger.LogWarning(
						$"The application is in state '{application.State}' and no valid document can be generated for it.");
					return null;
				}

				var pdfOptions = new PdfOptions
				{
					PageNumbers = PageNumbers.Numeric,
					HeaderOptions = new HeaderOptions
					{
						HeaderRepeat = HeaderRepeat.FirstPageOnly,
						HeaderHtml = PdfConstants.Header
					}
				};
				var pdf = _pdfGenerator.GenerateFromHtml(view, pdfOptions);
				return pdf.ToBytes();
			}
			else
			{

				_logger.LogWarning(
					$"No application found for id '{applicationId}'");
				return null;
			}
		}

		string GenerateView(Application application, string baseUri)
		{
			string path = string.Empty;
			var applicationViewModel = new ApplicationViewModel();
			if (application.State == ApplicationState.Pending)
			{
				path = GetPathProvider("PendingApplication");
				var pendingApplicationViewModel = new PendingApplicationViewModel();
				pendingApplicationViewModel = (PendingApplicationViewModel)SetApplicationViewModel(application);
				applicationViewModel = pendingApplicationViewModel;
			}
            else if (application.State == ApplicationState.Activated)
            {
				path = GetPathProvider("ActivatedApplication");
				var activatedApplicationViewModel = new ActivatedApplicationViewModel();
				activatedApplicationViewModel = (ActivatedApplicationViewModel)SetApplicationViewModel(application);
				applicationViewModel = activatedApplicationViewModel;
            }
            else if (application.State == ApplicationState.InReview)
            {
				path = GetPathProvider("InReviewApplication");
				var inReviewApplicationViewModel = new InReviewApplicationViewModel();
				inReviewApplicationViewModel = (InReviewApplicationViewModel)SetApplicationViewModel(application);
				inReviewApplicationViewModel.InReviewMessage = "Your application has been placed in review" +
										application.CurrentReview.Reason switch
										{
											{ } reason when reason.Contains("address") =>
												" pending outstanding address verification for FICA purposes.",
											{ } reason when reason.Contains("bank") =>
												" pending outstanding bank account verification.",
											_ =>
												" because of suspicious account behaviour. Please contact support ASAP."
										};
				inReviewApplicationViewModel.InReviewInformation = application.CurrentReview;
				applicationViewModel = inReviewApplicationViewModel;
			}			
			return _viewGenerator.GenerateFromPath($"{baseUri}{path}", applicationViewModel);
		}

		string GetPathProvider(string target) => _templatePathProvider.Get(target);

		ApplicationViewModel SetApplicationViewModel(Application application)
		 => new ApplicationViewModel()
		 {
			 ReferenceNumber = application.ReferenceNumber,
			 State = application.State.ToDescription(),
			 FullName = $"{application.Person.FirstName} {application.Person.Surname}",
			 AppliedOn = application.Date,
			 SupportEmail = _configuration.SupportEmail,
			 Signature = _configuration.Signature,
			 LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
			 PortfolioFunds = application.Products.SelectMany(s => s.Funds) ?? null,
			 PortfolioTotalAmount = (double?)application.Products
														.SelectMany(p => p.Funds)
														.Select(f => (f.Amount - f.Fees) * _configuration.TaxRate)
														.Sum() ?? 0
		 };
	}
}
