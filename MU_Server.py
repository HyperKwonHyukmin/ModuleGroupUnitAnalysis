# 연구실 서버 : 10.14.42.145

from flask import Flask, request, send_file, jsonify
import os
from datetime import datetime
from PythonModule.HookTrolley import HookTrolley
import sys


app = Flask(__name__)

# 파일 업로드 경로 설정 : 연구실 서버 컴퓨터에서 폴더 변경 필요
SERVER_FOLDER = r'C:\Coding\Python\Projects\ModuleUnit\ClientConnection'
if not os.path.exists(SERVER_FOLDER):
  os.makedirs(SERVER_FOLDER)

app.config['SERVER_FOLDER'] = SERVER_FOLDER


# 클라이언트 IP 확인 함수
def get_client_ip():
  # Flask에서 클라이언트 IP 확인
  client_ip = request.remote_addr
  # Proxy를 통한 연결일 경우, 헤더에서 실제 IP 가져오기
  if request.headers.get('X-Forwarded-For'):
    client_ip = request.headers['X-Forwarded-For'].split(',')[0]
  return client_ip


# 사용자 고유 폴더명 생성
def create_unique_folder(client_ip):
  # IP 주소에서 "."을 "_"로 변환하여 사용
  sanitized_ip = client_ip.replace('.', '_')
  current_time = datetime.now().strftime('%Y%m%d_%H%M%S')  # 날짜와 시간
  unique_folder_name = f"{sanitized_ip}_{current_time}"  # IP와 시간 결합
  return unique_folder_name


# 인포겟 프로그램에서 출력된 bdf 파일에서 해석에 필요한 요소들을 추출해내는 메써드
def InforgetMode(inforgetBDF):
  ModulePoint_idx_list = []
  lifting_method = None
  with open(inforgetBDF, 'r', encoding='utf8') as f:
    lines = f.readlines()
    for line_idx in range(len(lines)):
      if '$$Hydro' in lines[line_idx] or '$$Goliat' in lines[line_idx]:
        if '$$Hydro' in lines[line_idx]:
          lifting_method = 0
        elif '$$Goliat' in lines[line_idx]:
          lifting_method = 1
        ModulePoint_info_idx_start = line_idx + 1
        ModulePoint_idx_list.append(ModulePoint_info_idx_start)

      if '$$------------------------------------------------------------------------------$' in lines[line_idx]:
        ModulePoint_info_idx_end = line_idx
        ModulePoint_idx_list.append(ModulePoint_info_idx_end)
        break

  ModuleInfo_text = lines[ModulePoint_idx_list[0]: ModulePoint_idx_list[1]]

  ModuleInfo_dict = {}
  lineLength_list = []
  for line in ModuleInfo_text:
    clean_item = line.replace('$$', '').strip()
    parts = clean_item.split()
    category = int(parts[0].split('-')[0])  # '1-1'에서 '1' 추출
    data_value = (int(parts[1]), int(parts[2]))  # 두 번째와 세 번째 인덱스 값 (정수 변환)

    # 카테고리에 따라 그룹화하여 첫 번째 인덱스 값만 저장
    if category not in ModuleInfo_dict:
      ModuleInfo_dict[category] = [data_value[0]]
      lineLength_list.append(data_value[1])
    else:
      ModuleInfo_dict[category].append(data_value[0])

  ModuleInfo_list = list(ModuleInfo_dict.values())

  return inforgetBDF, ModuleInfo_list, lineLength_list, lifting_method


# 추출해 낸 정보를 사용하여 hookTrolley 해석을 수행하는 메써드
def ModuleAnalaysisRun(bdf):
  if not bdf.endswith(".bdf"):
    sys.exit()

  # 해석용 모듈 유닛 bdf는 _r을 붙인다.
  exportBDF = bdf.replace(".bdf", "_r.bdf")

  bdf, HookTrolley_list, lineLength, lifting_method = InforgetMode(bdf)
  HookTrolleyInstance = HookTrolley(bdf, exportBDF, HookTrolley_list, lineLength, Safety_Factor=1.2,
                                    lifting_method=lifting_method,
                                    analysis=True, debugPrint=True)  # trolley 모델
  HookTrolleyInstance.HookTrolleyRun()


# 파일 다운로드 엔드포인트
@app.route('/download/<filename>', methods=['GET'])
def download_file(filename):
  print('서버에서 찾고 있는 파일 : ', filename)
  if not os.path.exists(filename):
    return jsonify({"error": "File not found"}), 404
  return send_file(filename, as_attachment=True)


@app.route('/upload', methods=['POST'])
def upload_file():
  client_ip = get_client_ip()  # 클라이언트 IP 확인
  if 'file' not in request.files:
    return jsonify({"error": "No file part"}), 400
  file = request.files['file']
  if file.filename == '':
    return jsonify({"error": "No selected file"}), 400

  # 사용자별 고유명을 가지는 폴더를 생성
  client_folder_name = create_unique_folder(client_ip)
  client_folder = os.path.join(app.config['SERVER_FOLDER'], client_folder_name)
  if not os.path.exists(client_folder):
    os.makedirs(client_folder)

  # bdf 파일을 저장하고 Module Unit 해석 수행
  bdf_file = os.path.join(client_folder, file.filename)
  file.save(bdf_file)
  ModuleAnalaysisRun(bdf_file)

  return jsonify({"message": "File uploaded successfully", "filename": bdf_file}), 200


if __name__ == '__main__':
  app.run(host='0.0.0.0', port=5000, debug=True)
