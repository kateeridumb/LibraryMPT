import '../../../core/network/api_client.dart';
import '../../common/models.dart';

class CabinetRepository {
  CabinetRepository(this._apiClient);

  final ApiClient _apiClient;

  Future<CabinetData> loadCabinet() async {
    final response = await _apiClient.getJson(
      '/api/client/personal-cabinet',
      authorized: true,
    );
    return CabinetData.fromJson(response);
  }
}
