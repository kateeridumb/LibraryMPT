class UserSession {
  UserSession({
    required this.username,
    required this.roleName,
    required this.token,
  });

  final String username;
  final String roleName;
  final String token;

  bool get isStudentRole => roleName.trim().toLowerCase() == 'student';
}

class BookAuthor {
  BookAuthor({required this.firstName, required this.lastName});

  final String firstName;
  final String lastName;

  factory BookAuthor.fromJson(Map<String, dynamic> json) {
    return BookAuthor(
      firstName: (json['firstName'] ?? '').toString(),
      lastName: (json['lastName'] ?? '').toString(),
    );
  }

  String get fullName => '$firstName $lastName'.trim();
}

class BookPublisher {
  BookPublisher({required this.name});

  final String name;

  factory BookPublisher.fromJson(Map<String, dynamic> json) {
    return BookPublisher(
      name: (json['publisherName'] ?? json['name'] ?? '').toString().trim(),
    );
  }
}

class BookItem {
  BookItem({
    required this.bookId,
    required this.title,
    required this.description,
    required this.requiresSubscription,
    required this.imagePath,
    required this.publishYear,
    required this.categories,
    this.author,
    this.publisher,
    this.filePath,
  });

  final int bookId;
  final String title;
  final String description;
  final bool requiresSubscription;
  final String? imagePath;
  final int? publishYear;
  final List<BookCategoryItem> categories;
  final BookAuthor? author;
  final BookPublisher? publisher;
  final String? filePath;

  bool get hasAttachedFile => filePath != null && filePath!.trim().isNotEmpty;

  factory BookItem.fromJson(Map<String, dynamic> json) {
    return BookItem(
      bookId: (json['bookID'] ?? 0) as int,
      title: (json['title'] ?? '').toString(),
      description: (json['description'] ?? '').toString(),
      requiresSubscription: (json['requiresSubscription'] ?? false) as bool,
      imagePath: json['imagePath'] as String?,
      publishYear: json['publishYear'] as int?,
      categories: ((json['categories'] as List<dynamic>?) ?? const <dynamic>[])
          .whereType<Map<String, dynamic>>()
          .map(BookCategoryItem.fromJson)
          .toList(),
      author: json['author'] is Map<String, dynamic>
          ? BookAuthor.fromJson(json['author'] as Map<String, dynamic>)
          : null,
      publisher: json['publisher'] is Map<String, dynamic>
          ? BookPublisher.fromJson(json['publisher'] as Map<String, dynamic>)
          : null,
      filePath: json['filePath'] as String?,
    );
  }
}

class BookCategoryItem {
  BookCategoryItem({
    required this.categoryId,
    required this.categoryName,
  });

  final int categoryId;
  final String categoryName;

  factory BookCategoryItem.fromJson(Map<String, dynamic> json) {
    return BookCategoryItem(
      categoryId: (json['categoryID'] ?? 0) as int,
      categoryName: (json['categoryName'] ?? '').toString(),
    );
  }
}

class ClientIndexData {
  ClientIndexData({
    required this.books,
    required this.categories,
    required this.totalBooks,
    required this.hasSubscription,
    required this.subscriptionStatus,
    required this.readedBookIds,
    required this.personalPendingBookIds,
    required this.personalApprovedBookIds,
    required this.readedBooksCount,
  });

  final List<BookItem> books;
  final List<BookCategoryItem> categories;
  final int totalBooks;
  final bool hasSubscription;
  final String subscriptionStatus;
  final List<int> readedBookIds;
  final List<int> personalPendingBookIds;
  final List<int> personalApprovedBookIds;
  final int readedBooksCount;

  factory ClientIndexData.fromJson(Map<String, dynamic> json) {
    final booksJson = (json['books'] as List<dynamic>? ?? <dynamic>[]);
    return ClientIndexData(
      books: booksJson
          .whereType<Map<String, dynamic>>()
          .map(BookItem.fromJson)
          .toList(),
      categories: ((json['categories'] as List<dynamic>?) ?? const <dynamic>[])
          .whereType<Map<String, dynamic>>()
          .map(BookCategoryItem.fromJson)
          .toList(),
      totalBooks: (json['totalBooks'] ?? 0) as int,
      hasSubscription: (json['hasSubscription'] ?? false) as bool,
      subscriptionStatus: (json['subscriptionStatus'] ?? '').toString(),
      readedBookIds: _intList(json['readedBookIds']),
      personalPendingBookIds: _intList(json['personalPendingBookIds']),
      personalApprovedBookIds: _intList(json['personalApprovedBookIds']),
      readedBooksCount: (json['readed'] ?? 0) as int,
    );
  }
}

List<int> _intList(dynamic raw) {
  if (raw is! List<dynamic>) {
    return const <int>[];
  }
  final out = <int>[];
  for (final x in raw) {
    final n = int.tryParse('$x');
    if (n != null) {
      out.add(n);
    }
  }
  return out;
}

class BookDetailsData {
  BookDetailsData({
    required this.book,
    required this.canRead,
    required this.personalRequestStatus,
    required this.fileType,
  });

  final BookItem? book;
  final bool canRead;
  final String? personalRequestStatus;
  final String fileType;

  factory BookDetailsData.fromJson(Map<String, dynamic> json) {
    final bookJson = json['book'];
    return BookDetailsData(
      book: bookJson is Map<String, dynamic> ? BookItem.fromJson(bookJson) : null,
      canRead: (json['canRead'] ?? false) as bool,
      personalRequestStatus: json['personalRequestStatus'] as String?,
      fileType: (json['fileType'] ?? 'unknown').toString(),
    );
  }
}

class CabinetData {
  CabinetData({
    required this.readedBooks,
    required this.requestableBooks,
  });

  final List<BookItem> readedBooks;
  final List<BookItem> requestableBooks;

  factory CabinetData.fromJson(Map<String, dynamic> json) {
    final readedBooksJson = (json['readedBooks'] as List<dynamic>? ?? <dynamic>[]);
    final requestableBooksJson =
        (json['requestableBooks'] as List<dynamic>? ?? <dynamic>[]);

    return CabinetData(
      readedBooks: readedBooksJson
          .whereType<Map<String, dynamic>>()
          .map(BookItem.fromJson)
          .toList(),
      requestableBooks: requestableBooksJson
          .whereType<Map<String, dynamic>>()
          .map(BookItem.fromJson)
          .toList(),
    );
  }
}
