When getting new versions of either "Convergence.DataTransferObjects.dll" or "Convergence.Web.Services4.dll", they must be strong-signed.

To do this:
Download and install https://brutaldev.com/post/NET-Assembly-Strong-Name-Signer
Run the .Net Assembly Strong Name Signer:
	Key File = C:\TFSRoot\vdc-kdv3\Customers\Capstone\InternalTools\References\Convergence Training Code Signing Certificate 2019 End Only.pfx
	Password = Get from Secret Server
	Output = C:\TFSRoot\vdc-kdv3\Customers\Capstone\InternalTools\References\Signing\Output
Then replace the original DLL references with the strong signed ones.
