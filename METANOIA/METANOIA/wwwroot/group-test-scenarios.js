/* Bộ kịch bản tự động của trang group-test.html: mỗi kịch bản tạo nhóm "[Thử] ..." riêng, gọi API bằng các tài khoản
   thử (leader, m1, m2, viewer, outsider) và kiểm tra kết quả đúng như docs/GROUP_FEATURE.md.
   File này KHÔNG dùng DOM: nhận môi trường `env` từ trang (hoặc từ script kiểm tra bằng Node):
     env.call(as, method, path, body) -> Promise<{ ok, status, data }>
     env.has(key), env.userId(key), env.email(key)
   Cuối mỗi kịch bản, nhóm vừa tạo được lưu trữ (archive) để không ảnh hưởng lần chạy sau (nhóm đã lưu trữ không tính vào kiểm tra lịch). */
(function (root) {
  'use strict';

  const MIN = 60000;
  const LABEL = { leader: 'Leader', m1: 'Member 1', m2: 'Member 2', viewer: 'Viewer', outsider: 'Người ngoài' };

  // Mốc thời gian ngẫu nhiên trong tương lai xa (09:00 UTC) để các lần chạy không đụng nhau
  function runBase() {
    const d = new Date();
    d.setUTCHours(9, 0, 0, 0);
    d.setUTCDate(d.getUTCDate() + 40 + Math.floor(Math.random() * 300));
    return d.getTime();
  }
  const at = (base, minutes) => new Date(base + minutes * MIN).toISOString();
  const msg = (r) => {
    const d = r && r.data;
    if (!d) return '';
    return String(d.message || (typeof d === 'string' ? d : JSON.stringify(d))).slice(0, 200);
  };

  class T {
    constructor(env, scenario) {
      this.env = env;
      this.scenario = scenario;
      this.lines = [];
      this.groups = [];
      this.cleanups = [];
      this.archiveAs = 'leader';
    }

    call(as, method, path, body) { return this.env.call(as, method, path, body); }

    check(label, ok, detail) {
      this.lines.push({ ok: !!ok, label, detail: detail || '' });
      if (this.onLine) this.onLine(this.lines[this.lines.length - 1]);
      return !!ok;
    }

    status(label, r, expected) {
      return this.check(label, r.status === expected, `HTTP ${r.status}, mong ${expected}` + (r.status === expected ? '' : ' - ' + msg(r)));
    }

    code(label, r, status, code) {
      return this.check(label, r.status === status && r.data && r.data.code === code,
        `HTTP ${r.status}, code ${r.data && r.data.code}, mong ${status} ${code}` + (r.ok ? '' : ' - ' + msg(r)));
    }

    eq(label, actual, expected) {
      return this.check(label, JSON.stringify(actual) === JSON.stringify(expected), `nhận ${JSON.stringify(actual)}, mong ${JSON.stringify(expected)}`);
    }

    // Bước chuẩn bị: sai thì dừng kịch bản
    must(label, r, expected) {
      if (r.status !== expected) throw new Error(`${label}: HTTP ${r.status} (mong ${expected}) ${msg(r)}`);
      return r.data;
    }

    uid(key) { return this.env.userId(key); }

    async group(name, extra) {
      const r = await this.call('leader', 'POST', '/api/groups', Object.assign({ name: '[Thử] ' + name }, extra || {}));
      const g = this.must('Tạo nhóm', r, 201);
      this.groups.push(g.id);
      return g;
    }

    async join(groupId, key, role) {
      const inv = await this.call('leader', 'POST', `/api/groups/${groupId}/invitations`, { email: this.env.email(key), role: role || 'Member' });
      this.must(`Mời ${LABEL[key]}`, inv, 201);
      const mine = await this.call(key, 'GET', '/api/group-invitations');
      const found = (mine.data || []).find((i) => i.groupId === groupId);
      if (!found) throw new Error(`${LABEL[key]} không thấy lời mời vào nhóm`);
      this.must(`${LABEL[key]} chấp nhận lời mời`, await this.call(key, 'POST', `/api/group-invitations/${found.id}/accept`), 200);
    }

    async task(as, groupId, body) {
      return this.must('Tạo việc ' + (body.title || ''), await this.call(as, 'POST', `/api/groups/${groupId}/tasks`, body), 201);
    }

    async sub(as, taskId, title, assigneeKey) {
      return this.must('Tạo việc con ' + title,
        await this.call(as, 'POST', `/api/group-tasks/${taskId}/subtasks`, { title, assigneeId: assigneeKey ? this.uid(assigneeKey) : null }), 201);
    }

    async getTask(as, id) { return (await this.call(as, 'GET', `/api/group-tasks/${id}`)).data; }

    async getSub(as, id) { return (await this.call(as, 'GET', `/api/group-subtasks/${id}`)).data; }

    setStatus(as, taskId, status) { return this.call(as, 'PUT', `/api/group-tasks/${taskId}/status`, { status }); }

    setSubStatus(as, subId, status) { return this.call(as, 'PUT', `/api/group-subtasks/${subId}/status`, { status }); }

    start(as, taskId, subTaskId) { return this.call(as, 'POST', `/api/group-tasks/${taskId}/focus/start`, { subTaskId: subTaskId || null }); }

    stop(as, sessionId, stopReason, note) { return this.call(as, 'POST', `/api/group-focus/${sessionId}/stop`, { stopReason, note: note || null }); }

    async inbox(as) { return (await this.call(as, 'GET', '/api/notifications?take=100')).data || []; }
  }

  // ---------------------------------------------------------------- Kịch bản
  const scenarios = [];
  const add = (id, title, needs, run) => scenarios.push({ id, title, needs, run });

  add('roles', 'Ma trận quyền: Leader, Member, Viewer', ['leader', 'm1', 'viewer'], async (t) => {
    const g = await t.group('Ma trận quyền');
    await t.join(g.id, 'm1', 'Member');
    await t.join(g.id, 'viewer', 'Viewer');
    const task = await t.task('leader', g.id, { title: 'Việc A', assigneeId: t.uid('m1') });

    t.status('Viewer xem được việc', await t.call('viewer', 'GET', `/api/group-tasks/${task.id}`), 200);
    t.status('Viewer bình luận bị chặn (403)', await t.call('viewer', 'POST', `/api/group-tasks/${task.id}/comments`, { content: 'hi' }), 403);
    t.status('Viewer đổi status bị chặn (403)', await t.setStatus('viewer', task.id, 'InProgress'), 403);
    t.status('Viewer xem nhật ký bị chặn (403)', await t.call('viewer', 'GET', `/api/groups/${g.id}/activity`), 403);
    t.status('Viewer xem tiến độ được', await t.call('viewer', 'GET', `/api/groups/${g.id}/progress`), 200);

    t.status('Member tạo việc bị chặn (403)', await t.call('m1', 'POST', `/api/groups/${g.id}/tasks`, { title: 'x' }), 403);
    t.status('Member mời thành viên bị chặn (403)', await t.call('m1', 'POST', `/api/groups/${g.id}/invitations`, { email: 'a@b.test' }), 403);
    t.status('Member xóa thành viên bị chặn (403)', await t.call('m1', 'DELETE', `/api/groups/${g.id}/members/${t.uid('viewer')}`), 403);
    t.status('Member tạo giai đoạn bị chặn (403)', await t.call('m1', 'POST', `/api/groups/${g.id}/milestones`, { name: 'M', orderIndex: 1 }), 403);
    t.status('Member lưu trữ nhóm bị chặn (403)', await t.call('m1', 'POST', `/api/groups/${g.id}/archive`), 403);
    t.status('Member bình luận được', await t.call('m1', 'POST', `/api/group-tasks/${task.id}/comments`, { content: 'ok' }), 201);
    t.status('Member xem nhật ký được', await t.call('m1', 'GET', `/api/groups/${g.id}/activity`), 200);
  });

  add('outsider', 'Người ngoài nhóm nhận 404 ở mọi nơi', ['leader', 'outsider'], async (t) => {
    const g = await t.group('Người ngoài');
    const task = await t.task('leader', g.id, { title: 'Việc kín' });
    for (const [label, path] of [['nhóm', `/api/groups/${g.id}`], ['thành viên', `/api/groups/${g.id}/members`], ['danh sách việc', `/api/groups/${g.id}/tasks`],
      ['chi tiết việc', `/api/group-tasks/${task.id}`], ['tiến độ', `/api/groups/${g.id}/progress`], ['giờ làm', `/api/group-tasks/${task.id}/time`]]) {
      t.status(`Người ngoài xem ${label} → 404`, await t.call('outsider', 'GET', path), 404);
    }
    const mine = await t.call('outsider', 'GET', '/api/groups?includeArchived=true');
    t.check('Danh sách nhóm của người ngoài không có nhóm này', !(mine.data || []).some((x) => x.id === g.id));
  });

  add('invitations', 'Lời mời: đúng email, dùng lại, trùng, thu hồi', ['leader', 'm1', 'outsider'], async (t) => {
    const g = await t.group('Lời mời');
    const email = t.env.email('m1');
    const created = await t.call('leader', 'POST', `/api/groups/${g.id}/invitations`, { email: email.toUpperCase() });
    t.status('Leader mời Member (email viết hoa vẫn khớp) → 201', created, 201);
    const inv = created.data || {};
    t.eq('Vai trò mặc định là Member', inv.role, 'Member');
    t.check('Trả acceptToken một lần', !!inv.acceptToken);

    t.status('Người khác chấp nhận bằng id → 404', await t.call('outsider', 'POST', `/api/group-invitations/${inv.id}/accept`), 404);
    t.status('Người khác chấp nhận bằng token → 404', await t.call('outsider', 'POST', '/api/group-invitations/accept-by-token', { token: inv.acceptToken }), 404);
    const ok = await t.call('m1', 'POST', '/api/group-invitations/accept-by-token', { token: inv.acceptToken });
    t.status('Đúng người chấp nhận bằng token → 200', ok, 200);
    t.eq('Vai trò sau khi vào nhóm', ok.data && ok.data.myRole, 'Member');
    t.status('Dùng lại lời mời đã chấp nhận → 400', await t.call('m1', 'POST', `/api/group-invitations/${inv.id}/accept`), 400);
    t.status('Mời người đã là thành viên → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/invitations`, { email }), 400);

    const stranger = t.env.email('outsider');
    const first = await t.call('leader', 'POST', `/api/groups/${g.id}/invitations`, { email: stranger });
    t.status('Mời email mới → 201', first, 201);
    t.status('Mời lại email đang chờ → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/invitations`, { email: stranger }), 400);
    t.status('Email sai định dạng → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/invitations`, { email: 'khong-hop-le' }), 400);
    t.status('Vai trò không tồn tại → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/invitations`, { email: 'x@y.test', role: 'Boss' }), 400);

    const list = await t.call('leader', 'GET', `/api/groups/${g.id}/invitations`);
    t.check('Leader xem được danh sách lời mời', list.ok && list.data.length >= 2);
    t.status('Thu hồi lời mời → 204', await t.call('leader', 'DELETE', `/api/groups/${g.id}/invitations/${first.data.id}`), 204);
    t.status('Chấp nhận lời mời đã thu hồi → 400', await t.call('outsider', 'POST', `/api/group-invitations/${first.data.id}/accept`), 400);
    t.status('Từ chối lời mời đã thu hồi → 400', await t.call('outsider', 'POST', `/api/group-invitations/${first.data.id}/decline`), 400);

    const again = await t.call('leader', 'POST', `/api/groups/${g.id}/invitations`, { email: stranger });
    t.status('Sau khi thu hồi mời lại được → 201', again, 201);
    t.status('Người được mời từ chối → 204', await t.call('outsider', 'POST', `/api/group-invitations/${again.data.id}/decline`), 204);
  });

  add('lastleader', 'Luôn còn ít nhất một Leader', ['leader', 'm1'], async (t) => {
    const g = await t.group('Leader cuối');
    await t.join(g.id, 'm1', 'Member');
    t.status('Leader cuối rời nhóm → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/leave`), 400);
    t.status('Leader cuối tự hạ vai trò → 400', await t.call('leader', 'PUT', `/api/groups/${g.id}/members/${t.uid('leader')}`, { role: 'Member' }), 400);
    t.status('Leader cuối tự xóa mình → 400', await t.call('leader', 'DELETE', `/api/groups/${g.id}/members/${t.uid('leader')}`), 400);
    t.status('Vai trò không hợp lệ → 400', await t.call('leader', 'PUT', `/api/groups/${g.id}/members/${t.uid('m1')}`, { role: 'Boss' }), 400);
    t.status('Thăng Member lên Leader → 200', await t.call('leader', 'PUT', `/api/groups/${g.id}/members/${t.uid('m1')}`, { role: 'Leader' }), 200);
    t.status('Giờ có hai Leader, Leader đầu rời nhóm → 204', await t.call('leader', 'POST', `/api/groups/${g.id}/leave`), 204);
    t.status('Người đã rời không còn thấy nhóm → 404', await t.call('leader', 'GET', `/api/groups/${g.id}`), 404);
    t.archiveAs = 'm1';
  });

  add('assign', 'Giao việc: chỉ Leader/Member của nhóm', ['leader', 'm1', 'viewer', 'outsider'], async (t) => {
    const g = await t.group('Giao việc');
    await t.join(g.id, 'm1', 'Member');
    await t.join(g.id, 'viewer', 'Viewer');
    const task = await t.task('leader', g.id, { title: 'Việc cần giao' });

    t.status('Giao cho người ngoài nhóm → 400', await t.call('leader', 'PUT', `/api/group-tasks/${task.id}/assignee`, { assigneeId: t.uid('outsider') }), 400);
    t.status('Giao cho Viewer → 400', await t.call('leader', 'PUT', `/api/group-tasks/${task.id}/assignee`, { assigneeId: t.uid('viewer') }), 400);
    t.status('Tạo việc giao luôn cho người ngoài → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/tasks`, { title: 'x', assigneeId: t.uid('outsider') }), 400);
    const ok = await t.call('leader', 'PUT', `/api/group-tasks/${task.id}/assignee`, { assigneeId: t.uid('m1') });
    t.status('Giao cho Member → 200', ok, 200);
    t.check('Trả tên người được giao', !!(ok.data && ok.data.assigneeName));
    t.status('Member tự giao việc cho người khác → 403', await t.call('m1', 'PUT', `/api/group-tasks/${task.id}/assignee`, { assigneeId: t.uid('leader') }), 403);
    const mine = await t.call('m1', 'GET', '/api/my-group-tasks');
    t.check('"Việc của tôi" thấy việc vừa được giao', (mine.data.tasks || []).some((x) => x.taskId === task.id));
    const none = await t.call('leader', 'PUT', `/api/group-tasks/${task.id}/assignee`, { assigneeId: null });
    t.status('Bỏ giao việc → 200', none, 200);
    t.eq('Việc về chưa giao', none.data && none.data.assigneeId, null);
  });

  add('removal', 'Xóa thành viên hoặc hạ xuống Viewer thì việc về chưa giao', ['leader', 'm1', 'm2'], async (t) => {
    const g = await t.group('Xóa thành viên');
    await t.join(g.id, 'm1', 'Member');
    await t.join(g.id, 'm2', 'Member');
    const task = await t.task('leader', g.id, { title: 'Của m1', assigneeId: t.uid('m1') });
    const sub = await t.sub('leader', task.id, 'Phần của m1', 'm1');
    const task2 = await t.task('leader', g.id, { title: 'Của m2', assigneeId: t.uid('m2') });

    t.status('Leader xóa Member → 204', await t.call('leader', 'DELETE', `/api/groups/${g.id}/members/${t.uid('m1')}`), 204);
    t.eq('Việc của người bị xóa về chưa giao', (await t.getTask('leader', task.id)).assigneeId, null);
    t.eq('Việc con của người bị xóa về chưa giao', (await t.getSub('leader', sub.id)).assigneeId, null);
    t.status('Người bị xóa không còn vào nhóm (404)', await t.call('m1', 'GET', `/api/groups/${g.id}`), 404);

    t.status('Hạ Member xuống Viewer → 200', await t.call('leader', 'PUT', `/api/groups/${g.id}/members/${t.uid('m2')}`, { role: 'Viewer' }), 200);
    t.eq('Việc của người bị hạ về chưa giao', (await t.getTask('leader', task2.id)).assigneeId, null);
  });

  add('status', 'Status việc: Member chỉ đổi việc của mình', ['leader', 'm1'], async (t) => {
    const g = await t.group('Status việc');
    await t.join(g.id, 'm1', 'Member');
    const mine = await t.task('leader', g.id, { title: 'Của m1', assigneeId: t.uid('m1') });
    const notMine = await t.task('leader', g.id, { title: 'Của leader', assigneeId: t.uid('leader') });
    const withSubs = await t.task('leader', g.id, { title: 'Có việc con', assigneeId: t.uid('m1') });
    await t.sub('leader', withSubs.id, 'con 1', 'm1');

    const a = await t.setStatus('m1', mine.id, 'InProgress');
    t.status('Member đổi việc của mình sang InProgress → 200', a, 200);
    const b = await t.setStatus('m1', mine.id, 'Done');
    t.status('Member đóng việc của mình (Done) → 200', b, 200);
    t.check('Done có completedAt', !!(b.data && b.data.completedAt));
    t.status('Member đổi việc của người khác → 403', await t.setStatus('m1', notMine.id, 'InProgress'), 403);
    t.status('Member đổi việc có việc con → 403 (status tự suy)', await t.setStatus('m1', withSubs.id, 'InProgress'), 403);
    t.status('Leader đặt Skipped → 200', await t.setStatus('leader', notMine.id, 'Skipped'), 200);
    t.status('Skipped sang Done trực tiếp → 400', await t.setStatus('leader', notMine.id, 'Done'), 400);
    t.status('Status không tồn tại (Scheduled) → 400', await t.setStatus('leader', notMine.id, 'Scheduled'), 400);
    t.status('Mở lại Skipped về ToDo → 200', await t.setStatus('leader', notMine.id, 'ToDo'), 200);
  });

  add('derive', 'Việc con tự suy status việc cha', ['leader', 'm1', 'm2'], async (t) => {
    const g = await t.group('Suy status');
    await t.join(g.id, 'm1', 'Member');
    await t.join(g.id, 'm2', 'Member');
    const task = await t.task('leader', g.id, { title: 'Việc chung' });
    const s1 = await t.sub('leader', task.id, 'Phần của m1', 'm1');
    const s2 = await t.sub('leader', task.id, 'Phần của m2', 'm2');
    t.eq('Mới tạo: việc ToDo', (await t.getTask('leader', task.id)).status, 'ToDo');

    t.status('m1 bắt đầu phần của mình → 200', await t.setSubStatus('m1', s1.id, 'InProgress'), 200);
    t.eq('Việc cha tự InProgress', (await t.getTask('leader', task.id)).status, 'InProgress');
    t.status('m1 đổi phần của m2 → 403', await t.setSubStatus('m1', s2.id, 'Done'), 403);

    const done1 = await t.setSubStatus('m1', s1.id, 'Done');
    t.eq('Việc con Done ghi người hoàn thành', done1.data && done1.data.completedById, t.uid('m1'));
    t.eq('Còn một việc con mở: việc cha vẫn InProgress', (await t.getTask('leader', task.id)).status, 'InProgress');

    t.status('m2 xong phần cuối → 200', await t.setSubStatus('m2', s2.id, 'Done'), 200);
    const finished = await t.getTask('leader', task.id);
    t.eq('Mọi việc con xong: việc cha tự Done', finished.status, 'Done');
    t.check('Việc cha Done có completedAt', !!finished.completedAt);

    await t.sub('leader', task.id, 'Phần bổ sung', 'm1');
    const reopened = await t.getTask('leader', task.id);
    t.eq('Thêm việc con mới: việc đã Done được mở lại (InProgress)', reopened.status, 'InProgress');
    t.eq('Mở lại thì xóa completedAt', reopened.completedAt, null);
  });

  add('close', 'Đóng việc bằng tay và xóa việc con cuối', ['leader'], async (t) => {
    const g = await t.group('Đóng việc');
    const task = await t.task('leader', g.id, { title: 'Sẽ đóng' });
    const open = await t.sub('leader', task.id, 'Chưa làm', 'leader');
    const doing = await t.sub('leader', task.id, 'Đang làm', 'leader');
    await t.setSubStatus('leader', doing.id, 'InProgress');

    t.status('Leader đóng việc (Done) → 200', await t.setStatus('leader', task.id, 'Done'), 200);
    t.eq('Việc con chưa làm thành Skipped', (await t.getSub('leader', open.id)).status, 'Skipped');
    t.eq('Việc con đang làm thành Skipped', (await t.getSub('leader', doing.id)).status, 'Skipped');

    const skipTask = await t.task('leader', g.id, { title: 'Bỏ qua' });
    await t.setStatus('leader', skipTask.id, 'Skipped');
    await t.sub('leader', skipTask.id, 'con mới');
    t.eq('Việc Skipped không tự đổi khi thêm việc con', (await t.getTask('leader', skipTask.id)).status, 'Skipped');

    const last = await t.task('leader', g.id, { title: 'Xóa việc con cuối' });
    const s1 = await t.sub('leader', last.id, 'một');
    const s2 = await t.sub('leader', last.id, 'hai');
    await t.setSubStatus('leader', s1.id, 'Done');
    t.eq('Còn một việc con mở: InProgress', (await t.getTask('leader', last.id)).status, 'InProgress');
    t.status('Xóa việc con còn lại → 204', await t.call('leader', 'DELETE', `/api/group-subtasks/${s2.id}`), 204);
    t.eq('Xóa việc con cuối chưa xong: việc cha tự Done', (await t.getTask('leader', last.id)).status, 'Done');

    const order = await t.task('leader', g.id, { title: 'Thứ tự' });
    const o1 = await t.sub('leader', order.id, 'a');
    const o2 = await t.sub('leader', order.id, 'b');
    t.eq('Thứ tự mặc định xếp cuối (1 rồi 2)', [o1.orderIndex, o2.orderIndex], [1, 2]);
    t.status('Trùng số thứ tự → 400', await t.call('leader', 'POST', `/api/group-tasks/${order.id}/subtasks`, { title: 'trùng', orderIndex: 1 }), 400);
  });

  add('milestone', 'Giai đoạn: 409 khi còn việc mở, thứ tự, chuyển status', ['leader'], async (t) => {
    const g = await t.group('Giai đoạn');
    const other = await t.group('Nhóm khác');
    const m = (await t.call('leader', 'POST', `/api/groups/${g.id}/milestones`, { name: 'Giai đoạn 1', orderIndex: 1 })).data;
    const foreign = (await t.call('leader', 'POST', `/api/groups/${other.id}/milestones`, { name: 'Của nhóm khác', orderIndex: 1 })).data;
    const task = await t.task('leader', g.id, { title: 'Việc trong giai đoạn', milestoneId: m.id });

    t.status('Số thứ tự trùng → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/milestones`, { name: 'trùng', orderIndex: 1 }), 400);
    t.status('Planned nhảy thẳng Approved → 400', await t.call('leader', 'PUT', `/api/group-milestones/${m.id}`, { name: 'Giai đoạn 1', orderIndex: 1, status: 'Approved' }), 400);
    t.status('Việc gắn giai đoạn của nhóm khác → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/tasks`, { title: 'x', milestoneId: foreign.id }), 400);

    await t.setStatus('leader', task.id, 'InProgress');
    t.eq('Việc InProgress: giai đoạn Planned tự lên InProgress', (await t.call('leader', 'GET', `/api/group-milestones/${m.id}`)).data.status, 'InProgress');

    const body = { name: 'Giai đoạn 1', orderIndex: 1, status: 'Delivered' };
    const blocked = await t.call('leader', 'PUT', `/api/group-milestones/${m.id}`, body);
    t.code('Giao khi còn việc mở → 409 MILESTONE_HAS_OPEN_TASKS', blocked, 409, 'MILESTONE_HAS_OPEN_TASKS');
    t.check('409 kèm danh sách việc mở', !!(blocked.data && blocked.data.details && blocked.data.details.openTaskCount >= 1));
    body.confirmOpenTasks = true;
    const confirmed = await t.call('leader', 'PUT', `/api/group-milestones/${m.id}`, body);
    t.status('Gửi lại kèm confirmOpenTasks → 200', confirmed, 200);
    t.eq('Giai đoạn đã Delivered', confirmed.data && confirmed.data.status, 'Delivered');
    t.check('Delivered có deliveredAt', !!(confirmed.data && confirmed.data.deliveredAt));
    t.status('Giai đoạn đã Delivered không nhận thêm việc → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/tasks`, { title: 'muộn', milestoneId: m.id }), 400);
    const rev = await t.call('leader', 'PUT', `/api/group-milestones/${m.id}`, { name: 'Giai đoạn 1', orderIndex: 1, status: 'InRevision' });
    t.eq('Delivered sang InRevision tăng revisionCount', rev.data && rev.data.revisionCount, 1);
  });

  add('progress', 'Tiến độ nhóm và nhật ký hoạt động', ['leader', 'm1'], async (t) => {
    const g = await t.group('Tiến độ');
    await t.join(g.id, 'm1', 'Member');
    const empty = (await t.call('m1', 'GET', `/api/groups/${g.id}/progress`)).data;
    t.eq('Nhóm trống: 0 việc, 0%', [empty.totalTasks, empty.completedPercent], [0, 0]);

    const t1 = await t.task('leader', g.id, { title: 'Một', assigneeId: t.uid('m1'), estimatedMinutes: 60 });
    const t2 = await t.task('leader', g.id, { title: 'Hai', assigneeId: t.uid('m1') });
    await t.task('leader', g.id, { title: 'Ba' });
    const t4 = await t.task('leader', g.id, { title: 'Bốn' });
    await t.setStatus('m1', t1.id, 'Done');
    await t.setStatus('m1', t2.id, 'InProgress');
    await t.setStatus('leader', t4.id, 'Skipped');

    const p = (await t.call('leader', 'GET', `/api/groups/${g.id}/progress`)).data;
    t.eq('Tổng 4 việc', p.totalTasks, 4);
    t.eq('Theo status', p.tasksByStatus, { ToDo: 1, InProgress: 1, Done: 1, Skipped: 1 });
    t.eq('Còn lại (chưa Done, chưa Skipped) = 2', p.remainingTasks, 2);
    t.eq('Việc chưa giao còn mở = 1', p.unassignedTasks, 1);
    t.eq('% hoàn thành = Done / (tổng - Skipped) = 33.3', p.completedPercent, 33.3);
    t.eq('Ước lượng tổng', p.totalEstimatedMinutes, 60);
    const row = (p.members || []).find((x) => x.userId === t.uid('m1')) || {};
    t.eq('Member có 2 việc, 1 xong', [row.assignedTasks, row.doneTasks], [2, 1]);

    const log = (await t.call('m1', 'GET', `/api/groups/${g.id}/activity`)).data || [];
    t.eq('Nhật ký mới nhất trước: Member vừa đổi status', [log[0] && log[0].action, log[0] && log[0].actorId], ['StatusChanged', t.uid('leader')]);
    t.check('Nhật ký có tạo nhóm, vào nhóm, tạo việc', ['Group', 'Member', 'Task'].every((e) => log.some((l) => l.entityType === e)));
  });

  add('comments', 'Bình luận: tác giả sửa, Leader xóa', ['leader', 'm1', 'm2'], async (t) => {
    const g = await t.group('Bình luận');
    await t.join(g.id, 'm1', 'Member');
    await t.join(g.id, 'm2', 'Member');
    const task = await t.task('leader', g.id, { title: 'Việc' });
    const created = await t.call('m1', 'POST', `/api/group-tasks/${task.id}/comments`, { content: '  lần đầu  ' });
    t.status('Member bình luận → 201', created, 201);
    t.eq('Nội dung được cắt khoảng trắng', created.data.content, 'lần đầu');
    t.status('Bình luận rỗng → 400', await t.call('m1', 'POST', `/api/group-tasks/${task.id}/comments`, { content: ' ' }), 400);
    t.status('Người khác sửa → 403', await t.call('m2', 'PUT', `/api/group-comments/${created.data.id}`, { content: 'sửa trộm' }), 403);
    t.status('Người khác xóa → 403', await t.call('m2', 'DELETE', `/api/group-comments/${created.data.id}`), 403);
    const edited = await t.call('m1', 'PUT', `/api/group-comments/${created.data.id}`, { content: 'đã sửa' });
    t.status('Tác giả sửa → 200', edited, 200);
    t.check('Có editedAt', !!(edited.data && edited.data.editedAt));
    t.status('Leader xóa bình luận → 204', await t.call('leader', 'DELETE', `/api/group-comments/${created.data.id}`), 204);
    t.eq('Danh sách bình luận trống', (await t.call('m2', 'GET', `/api/group-tasks/${task.id}/comments`)).data, []);
  });

  add('archive', 'Nhóm đã lưu trữ chỉ xem được', ['leader'], async (t) => {
    const g = await t.group('Lưu trữ');
    const task = await t.task('leader', g.id, { title: 'Việc' });
    const archived = await t.call('leader', 'POST', `/api/groups/${g.id}/archive`);
    t.eq('Lưu trữ → status Archived', archived.data && archived.data.status, 'Archived');
    t.status('Lưu trữ lần nữa → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/archive`), 400);
    t.status('Tạo việc khi đã lưu trữ → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/tasks`, { title: 'mới' }), 400);
    t.status('Đổi status khi đã lưu trữ → 400', await t.setStatus('leader', task.id, 'InProgress'), 400);
    t.status('Vẫn xem được việc', await t.call('leader', 'GET', `/api/group-tasks/${task.id}`), 200);
    const hidden = await t.call('leader', 'GET', '/api/groups');
    t.check('Danh sách mặc định ẩn nhóm đã lưu trữ', !(hidden.data || []).some((x) => x.id === g.id));
    const shown = await t.call('leader', 'GET', '/api/groups?includeArchived=true');
    t.check('includeArchived=true thấy nhóm', (shown.data || []).some((x) => x.id === g.id));
    t.status('Khôi phục → 200', await t.call('leader', 'POST', `/api/groups/${g.id}/restore`), 200);
    t.status('Sau khôi phục đổi status được → 200', await t.setStatus('leader', task.id, 'InProgress'), 200);
  });

  add('notify', 'Thông báo kèm liên kết (dùng chung bảng Notification)', ['leader', 'm1'], async (t) => {
    const g = await t.group('Thông báo');
    await t.join(g.id, 'm1', 'Member');
    const task = await t.task('leader', g.id, { title: 'Việc A', assigneeId: t.uid('m1') });
    await t.setStatus('leader', task.id, 'InProgress');
    await t.setStatus('m1', task.id, 'Done');
    await t.call('leader', 'POST', `/api/group-tasks/${task.id}/comments`, { content: `Nhờ @${t.env.email('m1')} xem lại` });

    const box = (await t.inbox('m1')).filter((n) => n.groupId === g.id);
    const types = box.map((n) => n.type);
    t.check('Có GroupInvited (chỉ có groupId)', box.some((n) => n.type === 'GroupInvited' && !n.groupTaskId));
    t.check('Có GroupAssigned kèm groupId và groupTaskId', box.some((n) => n.type === 'GroupAssigned' && n.groupTaskId === task.id));
    t.eq('GroupStatusChanged đúng 1 (chỉ khi người KHÁC đổi, tự đổi không báo)', types.filter((x) => x === 'GroupStatusChanged').length, 1);
    t.check('Có GroupMention (@email trong bình luận)', box.some((n) => n.type === 'GroupMention' && n.groupTaskId === task.id));
    const leaderBox = (await t.inbox('leader')).filter((n) => n.groupId === g.id);
    t.check('Leader tự thao tác không nhận thông báo về chính mình', !leaderBox.some((n) => n.type === 'GroupAssigned' || n.type === 'GroupStatusChanged'));
  });

  add('focus', 'Bấm giờ: Start/Stop điều khiển status', ['leader', 'm1'], async (t) => {
    const g = await t.group('Bấm giờ');
    await t.join(g.id, 'm1', 'Member');
    const ms = (await t.call('leader', 'POST', `/api/groups/${g.id}/milestones`, { name: 'M1', orderIndex: 1 })).data;
    const task = await t.task('leader', g.id, { title: 'Có việc con', milestoneId: ms.id, estimatedMinutes: 120 });
    const s1 = await t.sub('leader', task.id, 'Phần 1', 'm1');
    const s2 = await t.sub('leader', task.id, 'Phần 2', 'm1');

    const started = await t.start('m1', task.id, s1.id);
    t.status('Start trên việc con của mình → 201', started, 201);
    t.eq('Việc con thành InProgress', started.data && started.data.subTaskStatus, 'InProgress');
    t.eq('Việc cha thành InProgress', started.data && started.data.taskStatus, 'InProgress');
    t.eq('Giai đoạn Planned tự lên InProgress', (await t.call('leader', 'GET', `/api/group-milestones/${ms.id}`)).data.status, 'InProgress');

    const current = await t.call('m1', 'GET', '/api/group-focus/current');
    t.status('GET current → 200', current, 200);
    t.eq('current trỏ đúng phiên và việc con', [current.data.sessionId, current.data.subTaskTitle], [started.data.sessionId, 'Phần 1']);
    t.status('Người khác không có phiên đang chạy → 204', await t.call('leader', 'GET', '/api/group-focus/current'), 204);

    const stopped = await t.stop('m1', started.data.sessionId, 'Completed');
    t.status('Stop Completed → 200', stopped, 200);
    t.eq('Việc con thành Done', stopped.data && stopped.data.subTaskStatus, 'Done');
    t.eq('Còn việc con mở: việc cha vẫn InProgress', stopped.data && stopped.data.taskStatus, 'InProgress');
    t.status('Sau khi dừng: current → 204', await t.call('m1', 'GET', '/api/group-focus/current'), 204);

    const second = await t.start('m1', task.id, s2.id);
    const done = await t.stop('m1', second.data.sessionId, 'Completed');
    t.eq('Xong việc con cuối: việc cha tự Done', done.data && done.data.taskStatus, 'Done');

    const alone = await t.task('leader', g.id, { title: 'Không có việc con', assigneeId: t.uid('m1') });
    const a1 = await t.start('m1', alone.id);
    t.eq('Việc không có việc con: Start đưa chính việc sang InProgress', a1.data && a1.data.taskStatus, 'InProgress');
    const a2 = await t.stop('m1', a1.data.sessionId, 'Interrupted');
    t.eq('Stop Interrupted không đổi status', a2.data && a2.data.taskStatus, 'InProgress');
    const a3 = await t.start('m1', alone.id);
    const a4 = await t.stop('m1', a3.data.sessionId, 'Completed');
    t.eq('Stop Completed: việc không có việc con thành Done', a4.data && a4.data.taskStatus, 'Done');
    t.status('Việc đã Done không Start được → 400', await t.start('m1', alone.id), 400);
  });

  add('focusrules', 'Bấm giờ: ai được bấm, một phiên một lúc, luật Stop', ['leader', 'm1', 'm2', 'viewer', 'outsider'], async (t) => {
    const g = await t.group('Luật bấm giờ');
    await t.join(g.id, 'm1', 'Member');
    await t.join(g.id, 'm2', 'Member');
    await t.join(g.id, 'viewer', 'Viewer');
    const task = await t.task('leader', g.id, { title: 'Việc', assigneeId: t.uid('m1') });
    const mine = await t.sub('leader', task.id, 'Của m1', 'm1');
    const theirs = await t.sub('leader', task.id, 'Của m2', 'm2');
    const free = await t.sub('leader', task.id, 'Chưa giao');
    const other = await t.task('leader', g.id, { title: 'Việc khác' });
    const foreign = await t.sub('leader', other.id, 'Con của việc khác', 'm1');

    t.status('Việc có việc con mà không chọn việc con → 400', await t.start('m1', task.id), 400);
    t.status('Bấm việc con của người khác → 403', await t.start('m1', task.id, theirs.id), 403);
    t.status('Viewer bấm giờ → 403', await t.start('viewer', task.id, mine.id), 403);
    t.status('Người ngoài bấm giờ → 404', await t.start('outsider', task.id, mine.id), 404);
    t.status('Việc con không thuộc việc này → 400', await t.start('m1', task.id, foreign.id), 400);

    const unassigned = await t.start('m1', task.id, free.id);
    t.status('Việc con chưa giao: người được giao việc cha bấm được → 201', unassigned, 201);
    const conflict = await t.start('m1', task.id, mine.id);
    t.code('Đang có phiên: Start tiếp → 409 FOCUS_SESSION_CONFLICT', conflict, 409, 'FOCUS_SESSION_CONFLICT');
    t.eq('409 chỉ rõ phiên của nhóm', conflict.data && conflict.data.details && conflict.data.details.source, 'Group');

    const sessionId = unassigned.data.sessionId;
    t.status('Dừng Blocked không có ghi chú → 400', await t.stop('m1', sessionId, 'Blocked'), 400);
    t.status('Dừng Other với ghi chú trống → 400', await t.stop('m1', sessionId, 'Other', '  '), 400);
    t.status('Người khác dừng phiên của m1 → 404', await t.stop('m2', sessionId, 'Interrupted'), 404);
    t.status('Leader dừng phiên của m1 → 404', await t.stop('leader', sessionId, 'Interrupted'), 404);
    t.status('Dừng Blocked kèm ghi chú → 200', await t.stop('m1', sessionId, 'Blocked', 'Chờ khách phản hồi'), 200);
    t.status('Dừng lần hai → 409', await t.stop('m1', sessionId, 'Interrupted'), 409);

    const own = await t.start('m2', task.id, theirs.id);
    t.status('Người được giao việc con bấm được kể cả không được giao việc cha → 201', own, 201);
    await t.stop('m2', own.data.sessionId, 'Interrupted');
  });

  add('timereport', 'Giờ làm theo từng người và từng việc con', ['leader', 'm1', 'm2', 'viewer'], async (t) => {
    const g = await t.group('Báo cáo giờ');
    await t.join(g.id, 'm1', 'Member');
    await t.join(g.id, 'm2', 'Member');
    await t.join(g.id, 'viewer', 'Viewer');
    const task = await t.task('leader', g.id, { title: 'Báo cáo', estimatedMinutes: 300 });
    const sa = await t.sub('leader', task.id, 'Phần A', 'm1');
    const sb = await t.sub('leader', task.id, 'Phần B', 'm2');

    const s1 = await t.start('m1', task.id, sa.id);
    await t.stop('m1', s1.data.sessionId, 'Interrupted');
    const s2 = await t.start('m2', task.id, sb.id);
    await t.stop('m2', s2.data.sessionId, 'Interrupted');
    const s3 = await t.start('m1', task.id, sa.id);
    await t.stop('m1', s3.data.sessionId, 'Interrupted');

    const time = await t.call('viewer', 'GET', `/api/group-tasks/${task.id}/time`);
    t.status('Viewer xem được báo cáo giờ → 200', time, 200);
    const d = time.data || {};
    t.eq('3 phiên đã kết thúc', d.sessionCount, 3);
    t.eq('Ước lượng của việc', d.estimatedMinutes, 300);
    const byUser = d.byUser || [];
    t.eq('m1 có 2 phiên', (byUser.find((x) => x.userId === t.uid('m1')) || {}).sessionCount, 2);
    t.eq('m2 có 1 phiên', (byUser.find((x) => x.userId === t.uid('m2')) || {}).sessionCount, 1);
    t.check('Chia theo việc con có tiêu đề', (d.bySubTask || []).some((x) => x.subTaskId === sb.id && x.title === 'Phần B'));
    t.eq('Danh sách phiên đủ 3 dòng', (d.sessions || []).length, 3);
    const p = (await t.call('leader', 'GET', `/api/groups/${g.id}/progress`)).data || {};
    t.check('Tiến độ nhóm có tổng giờ làm', typeof p.totalTrackedMinutes === 'number' && p.totalEstimatedMinutes === 300);
    t.check('Tiến độ từng thành viên có trackedMinutes', (p.members || []).every((m) => typeof m.trackedMinutes === 'number'));
  });

  add('focusremove', 'Bị xóa khỏi nhóm: phiên đang chạy tự dừng', ['leader', 'm1'], async (t) => {
    const g1 = await t.group('Xóa khi đang chạy 1');
    const g2 = await t.group('Xóa khi đang chạy 2');
    await t.join(g1.id, 'm1', 'Member');
    await t.join(g2.id, 'm1', 'Member');
    const t1 = await t.task('leader', g1.id, { title: 'Việc nhóm 1', assigneeId: t.uid('m1') });
    const t2 = await t.task('leader', g2.id, { title: 'Việc nhóm 2', assigneeId: t.uid('m1') });

    t.status('m1 bắt đầu việc nhóm 1 → 201', await t.start('m1', t1.id), 201);
    t.status('Leader xóa m1 khỏi nhóm 1 → 204', await t.call('leader', 'DELETE', `/api/groups/${g1.id}/members/${t.uid('m1')}`), 204);
    t.status('Phiên đang chạy tự dừng (current → 204)', await t.call('m1', 'GET', '/api/group-focus/current'), 204);
    const time = await t.call('leader', 'GET', `/api/group-tasks/${t1.id}/time`);
    t.eq('Phiên trong báo cáo đã kết thúc', time.data && time.data.sessions && time.data.sessions[0] && time.data.sessions[0].isRunning, false);
    const next = await t.start('m1', t2.id);
    t.status('m1 bấm giờ được ở nhóm khác → 201', next, 201);
    await t.stop('m1', next.data.sessionId, 'Interrupted');
  });

  add('focusarchive', 'Nhóm lưu trữ: không Start, nhưng đang chạy vẫn Stop được', ['leader', 'm1'], async (t) => {
    const g = await t.group('Lưu trữ khi đang chạy');
    await t.join(g.id, 'm1', 'Member');
    const task = await t.task('leader', g.id, { title: 'Việc', assigneeId: t.uid('m1') });
    const started = await t.start('m1', task.id);
    await t.call('leader', 'POST', `/api/groups/${g.id}/archive`);
    const stopped = await t.stop('m1', started.data.sessionId, 'Completed');
    t.status('Vẫn dừng được phiên → 200', stopped, 200);
    t.eq('Nhóm lưu trữ: giờ được ghi nhưng status không đổi', (await t.getTask('leader', task.id)).status, 'InProgress');
    t.status('Start khi đã lưu trữ → 400', await t.start('m1', task.id), 400);
  });

  add('fixedvalidate', 'Giờ cố định: đủ hai đầu, kết thúc sau bắt đầu', ['leader'], async (t) => {
    const g = await t.group('Giờ cố định');
    const base = runBase();
    t.status('Chỉ có giờ bắt đầu → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/tasks`, { title: 'x', fixedStart: at(base, 0) }), 400);
    t.status('Kết thúc trước bắt đầu → 400', await t.call('leader', 'POST', `/api/groups/${g.id}/tasks`, { title: 'x', fixedStart: at(base, 0), fixedEnd: at(base, -60) }), 400);
    const ok = await t.call('leader', 'POST', `/api/groups/${g.id}/tasks`, { title: 'x', fixedStart: at(base, 0), fixedEnd: at(base, 60) });
    t.status('Đủ hai đầu → 201', ok, 201);
    t.eq('Trả lại giờ cố định', [Date.parse(ok.data.fixedStart), Date.parse(ok.data.fixedEnd)], [base, base + 60 * MIN]);
  });

  add('busy', 'Kiểm tra lịch khi giao việc: trùng việc nhóm khác, xác nhận', ['leader', 'm1', 'm2'], async (t) => {
    const g1 = await t.group('Lịch nhóm 1');
    const g2 = await t.group('Lịch nhóm 2');
    await t.join(g1.id, 'm1', 'Member');
    await t.join(g2.id, 'm1', 'Member');
    await t.join(g1.id, 'm2', 'Member');
    const base = runBase();
    await t.task('leader', g1.id, { title: 'Họp bí mật', assigneeId: t.uid('m1'), fixedStart: at(base, 0), fixedEnd: at(base, 60) });

    const overlap = { title: 'Họp 2', assigneeId: t.uid('m1'), fixedStart: at(base, 30), fixedEnd: at(base, 90) };
    const blocked = await t.call('leader', 'POST', `/api/groups/${g2.id}/tasks`, overlap);
    t.code('Giao việc trùng giờ (khác nhóm) → 409 GROUP_ASSIGNEE_BUSY', blocked, 409, 'GROUP_ASSIGNEE_BUSY');
    const detail = JSON.stringify(blocked.data || {});
    t.check('409 chỉ có khoảng bận và nguồn GroupTask', detail.includes('GroupTask'));
    t.check('409 không lộ tiêu đề việc của người nhận', !detail.includes('Họp bí mật'));

    const touching = await t.call('leader', 'POST', `/api/groups/${g2.id}/tasks`, { title: 'Họp 3', assigneeId: t.uid('m1'), fixedStart: at(base, 60), fixedEnd: at(base, 120) });
    t.status('Chạm đầu mút không tính là trùng → 201', touching, 201);
    overlap.confirmConflicts = true;
    const forced = await t.call('leader', 'POST', `/api/groups/${g2.id}/tasks`, overlap);
    t.status('Gửi lại kèm confirmConflicts → 201', forced, 201);

    // Xem trước và giao sau
    const meeting = await t.task('leader', g1.id, { title: 'Họp nhóm', fixedStart: at(base, 30), fixedEnd: at(base, 90) });
    const check = await t.call('leader', 'GET', `/api/group-tasks/${meeting.id}/assignee-check?assigneeId=${t.uid('m1')}`);
    t.status('Leader xem trước lịch người nhận → 200', check, 200);
    t.eq('Xem trước: người nhận đang bận', [check.data.isBusy, check.data.needsConfirmation], [true, true]);
    const free = await t.call('leader', 'GET', `/api/group-tasks/${meeting.id}/assignee-check?assigneeId=${t.uid('m2')}`);
    t.eq('Xem trước: người khác còn rảnh', free.data && free.data.isBusy, false);
    t.status('Member xem trước lịch người khác → 403', await t.call('m2', 'GET', `/api/group-tasks/${meeting.id}/assignee-check?assigneeId=${t.uid('m1')}`), 403);
    t.code('Đổi người nhận sang người đang bận → 409', await t.call('leader', 'PUT', `/api/group-tasks/${meeting.id}/assignee`, { assigneeId: t.uid('m1') }), 409, 'GROUP_ASSIGNEE_BUSY');
    t.status('Giao cho người rảnh → 200', await t.call('leader', 'PUT', `/api/group-tasks/${meeting.id}/assignee`, { assigneeId: t.uid('m2') }), 200);

    // Sửa giờ của việc đã giao cũng kiểm tra lại
    const moveBody = { title: 'Họp nhóm', fixedStart: at(base, 10), fixedEnd: at(base, 50) };
    const m2task = await t.task('leader', g1.id, { title: 'Việc của m1', assigneeId: t.uid('m1'), fixedStart: at(base, 600), fixedEnd: at(base, 660) });
    t.code('Dời việc đã giao vào giờ đang bận → 409', await t.call('leader', 'PUT', `/api/group-tasks/${m2task.id}`, moveBody), 409, 'GROUP_ASSIGNEE_BUSY');
    t.status('Đổi tiêu đề (không đổi giờ) không kiểm tra lại → 200', await t.call('leader', 'PUT', `/api/group-tasks/${m2task.id}`, { title: 'Đổi tên', fixedStart: m2task.fixedStart, fixedEnd: m2task.fixedEnd }), 200);
  });

  add('workload', 'Cảnh báo khối lượng việc trong tuần', ['leader', 'm1', 'm2'], async (t) => {
    const g = await t.group('Khối lượng');
    await t.join(g.id, 'm1', 'Member');
    await t.join(g.id, 'm2', 'Member');
    const deadline = at(runBase(), 8 * 60);
    // Giờ làm mặc định 8h đến 18h, 5 ngày một tuần = 3000 phút. Nếu người nhận đã đổi giờ làm thì con số khác nhưng 2000 + 1500 vẫn vượt mức thường gặp.
    await t.task('leader', g.id, { title: 'Nặng 1', assigneeId: t.uid('m1'), deadline, estimatedMinutes: 2000 });
    const heavy = { title: 'Nặng 2', assigneeId: t.uid('m1'), deadline, estimatedMinutes: 1500 };
    const blocked = await t.call('leader', 'POST', `/api/groups/${g.id}/tasks`, heavy);
    t.code('Tổng 3500 phút vượt giờ làm một tuần → 409', blocked, 409, 'GROUP_ASSIGNEE_BUSY');
    t.check('409 kèm số liệu khối lượng', !!(blocked.data && blocked.data.details && blocked.data.details.workload && blocked.data.details.workload.isOverloaded));

    const other = Object.assign({}, heavy, { assigneeId: t.uid('m2') });
    t.status('Người còn rảnh nhận được → 201', await t.call('leader', 'POST', `/api/groups/${g.id}/tasks`, other), 201);
    heavy.confirmConflicts = true;
    t.status('Leader xác nhận vẫn giao → 201', await t.call('leader', 'POST', `/api/groups/${g.id}/tasks`, heavy), 201);
  });

  add('changereq', 'Đề xuất đổi giờ: gửi, duyệt, từ chối, rút lại', ['leader', 'm1', 'm2', 'viewer'], async (t) => {
    const g = await t.group('Đề xuất đổi giờ');
    await t.join(g.id, 'm1', 'Member');
    await t.join(g.id, 'm2', 'Member');
    await t.join(g.id, 'viewer', 'Viewer');
    const base = runBase();
    const deadline = at(base, 8 * 60);
    const task = await t.task('leader', g.id, { title: 'Họp khách', assigneeId: t.uid('m1'), fixedStart: at(base, 0), fixedEnd: at(base, 60), deadline });
    const url = `/api/group-tasks/${task.id}/change-requests`;

    t.status('Không phải người được giao → 403', await t.call('m2', 'POST', url, { newDeadline: at(base, 24 * 60), reason: 'x' }), 403);
    t.status('Viewer đề xuất → 403', await t.call('viewer', 'POST', url, { newDeadline: at(base, 24 * 60), reason: 'x' }), 403);
    t.status('Không có giá trị mới → 400', await t.call('m1', 'POST', url, { reason: 'trống' }), 400);
    t.status('Giờ cố định thiếu đầu kết thúc → 400', await t.call('m1', 'POST', url, { newFixedStart: at(base, 120), reason: 'thiếu' }), 400);
    t.status('Giờ kết thúc trước bắt đầu → 400', await t.call('m1', 'POST', url, { newFixedStart: at(base, 120), newFixedEnd: at(base, 60), reason: 'ngược' }), 400);
    t.status('Lý do rỗng → 400', await t.call('m1', 'POST', url, { newDeadline: at(base, 24 * 60), reason: ' ' }), 400);
    t.status('Giống giá trị hiện tại → 400', await t.call('m1', 'POST', url, { newFixedStart: at(base, 0), newFixedEnd: at(base, 60), reason: 'giống' }), 400);

    const created = await t.call('m1', 'POST', url, { newFixedStart: at(base, 120), newFixedEnd: at(base, 180), reason: 'Trùng lịch học' });
    t.status('Người được giao đề xuất → 201', created, 201);
    const req = created.data || {};
    t.eq('Đề xuất ở trạng thái Pending', req.status, 'Pending');
    t.eq('Ghi giờ hiện tại của việc', Date.parse(req.currentFixedStart), base);
    t.status('Đề xuất thứ hai khi còn đề xuất chờ → 400', await t.call('m1', 'POST', url, { newDeadline: at(base, 48 * 60), reason: 'lần nữa' }), 400);

    const leaderBox = (await t.inbox('leader')).filter((n) => n.type === 'GroupChangeRequested' && n.groupTaskId === task.id);
    t.check('Leader nhận thông báo GroupChangeRequested kèm liên kết', leaderBox.length === 1 && leaderBox[0].groupId === g.id);
    t.eq('Leader thấy đề xuất đang chờ', ((await t.call('leader', 'GET', `/api/groups/${g.id}/change-requests?status=Pending`)).data || []).length, 1);
    t.eq('Member chỉ thấy đề xuất của mình (m2 không thấy)', ((await t.call('m2', 'GET', `/api/groups/${g.id}/change-requests`)).data || []).length, 0);
    t.status('Viewer xem danh sách đề xuất → 403', await t.call('viewer', 'GET', `/api/groups/${g.id}/change-requests`), 403);
    t.status('Member duyệt đề xuất → 403', await t.call('m1', 'POST', `/api/group-change-requests/${req.id}/approve`, {}), 403);

    t.status('Từ chối không có lý do → 400', await t.call('leader', 'POST', `/api/group-change-requests/${req.id}/reject`, { note: ' ' }), 400);
    const rejected = await t.call('leader', 'POST', `/api/group-change-requests/${req.id}/reject`, { note: 'Deadline cứng' });
    t.status('Từ chối kèm lý do → 200', rejected, 200);
    t.eq('Việc giữ nguyên giờ sau khi từ chối', Date.parse((await t.getTask('leader', task.id)).fixedStart), base);
    const noticeR = (await t.inbox('m1')).find((n) => n.type === 'GroupChangeDecided' && n.groupTaskId === task.id);
    t.check('Người đề xuất nhận thông báo kèm lý do từ chối', !!noticeR && /Deadline cứng/.test(noticeR.message));
    t.status('Duyệt đề xuất đã xử lý → 400', await t.call('leader', 'POST', `/api/group-change-requests/${req.id}/approve`, {}), 400);

    const second = (await t.call('m1', 'POST', url, { newFixedStart: at(base, 120), newFixedEnd: at(base, 180), reason: 'Lần hai' })).data;
    t.status('Sau khi bị từ chối được gửi đề xuất mới → có id', { status: second && second.id ? 201 : 0 }, 201);
    const approved = await t.call('leader', 'POST', `/api/group-change-requests/${second.id}/approve`, { note: 'OK' });
    t.status('Leader duyệt → 200', approved, 200);
    t.eq('Việc đổi sang giờ mới', Date.parse((await t.getTask('leader', task.id)).fixedStart), base + 120 * MIN);
    t.status('Rút lại đề xuất đã duyệt → 400', await t.call('m1', 'POST', `/api/group-change-requests/${second.id}/cancel`), 400);

    const third = (await t.call('m1', 'POST', url, { newDeadline: at(base, 72 * 60), reason: 'Lần ba' })).data;
    t.status('Người khác rút đề xuất của m1 → 403', await t.call('m2', 'POST', `/api/group-change-requests/${third.id}/cancel`), 403);
    const cancelled = await t.call('m1', 'POST', `/api/group-change-requests/${third.id}/cancel`);
    t.eq('Người đề xuất rút lại → Cancelled', cancelled.data && cancelled.data.status, 'Cancelled');
    t.check('Nhật ký có ChangeApproved và ChangeRejected', await (async () => {
      const log = (await t.call('leader', 'GET', `/api/groups/${g.id}/activity?take=200`)).data || [];
      return log.some((l) => l.action === 'ChangeApproved') && log.some((l) => l.action === 'ChangeRejected');
    })());
  });

  add('changebusy', 'Duyệt đề xuất vào giờ đang bận: 409 rồi xác nhận', ['leader', 'm1'], async (t) => {
    const g = await t.group('Duyệt vào giờ bận');
    await t.join(g.id, 'm1', 'Member');
    const base = runBase();
    await t.task('leader', g.id, { title: 'Đã có giờ', assigneeId: t.uid('m1'), fixedStart: at(base, 0), fixedEnd: at(base, 60) });
    const meeting = await t.task('leader', g.id, { title: 'Họp', assigneeId: t.uid('m1'), fixedStart: at(base, 240), fixedEnd: at(base, 300) });
    const req = (await t.call('m1', 'POST', `/api/group-tasks/${meeting.id}/change-requests`, { newFixedStart: at(base, 15), newFixedEnd: at(base, 75), reason: 'Dời sớm' })).data;

    const blocked = await t.call('leader', 'POST', `/api/group-change-requests/${req.id}/approve`, {});
    t.code('Duyệt vào giờ trùng lịch người nhận → 409', blocked, 409, 'GROUP_ASSIGNEE_BUSY');
    t.eq('Việc vẫn giữ giờ cũ', Date.parse((await t.getTask('leader', meeting.id)).fixedStart), base + 240 * MIN);
    t.eq('Đề xuất vẫn Pending', (((await t.call('leader', 'GET', `/api/group-tasks/${meeting.id}/change-requests`)).data || [])[0] || {}).status, 'Pending');
    const forced = await t.call('leader', 'POST', `/api/group-change-requests/${req.id}/approve`, { confirmConflicts: true });
    t.status('Duyệt kèm confirmConflicts → 200', forced, 200);
    t.eq('Việc đổi sang giờ mới', Date.parse((await t.getTask('leader', meeting.id)).fixedStart), base + 15 * MIN);
  });

  add('personal', 'Lịch cá nhân và phiên cá nhân ảnh hưởng việc nhóm (cần Domain)', ['leader', 'm1'], async (t) => {
    const domains = await t.call('m1', 'GET', '/api/domains');
    const domain = (domains.data || []).find((d) => d.isActive !== false);
    if (!domain) { t.check('Bỏ qua: chưa có Domain nào để tạo việc cá nhân (tạo Domain bằng tài khoản Admin)', true); return; }

    const g = await t.group('Lịch cá nhân');
    await t.join(g.id, 'm1', 'Member');
    const base = runBase();

    // Việc và slot cá nhân của m1 (có tên riêng tư)
    const project = (await t.call('m1', 'POST', '/api/projects', { name: '[Thử] Bí mật riêng tư' })).data;
    if (!project || !project.id) { t.check('Bỏ qua: m1 không tạo được dự án cá nhân (có thể hết hạn mức gói)', true); return; }
    t.cleanups.push(() => t.call('m1', 'POST', `/api/projects/${project.id}/archive`));
    const personal = (await t.call('m1', 'POST', `/api/projects/${project.id}/tasks`, {
      title: 'Việc cá nhân riêng tư', taskType: 'Flexible', domainId: domain.id, deadline: at(base, 24 * 60 * 30), estimatedMinutes: 60
    })).data;
    const slot = await t.call('m1', 'POST', `/api/tasks/${personal.id}/schedule-slots`, { startTime: at(base, 0), endTime: at(base, 60), isManualOverride: true, status: 'Approved' });
    t.status('m1 có slot cá nhân Approved lúc 09:00 → 201', slot, 201);
    if (slot.data && slot.data.id) t.cleanups.push(() => t.call('m1', 'DELETE', `/api/schedule-slots/${slot.data.id}`));

    const meeting = await t.task('leader', g.id, { title: 'Họp nhóm', fixedStart: at(base, 30), fixedEnd: at(base, 90) });
    const check = await t.call('leader', 'GET', `/api/group-tasks/${meeting.id}/assignee-check?assigneeId=${t.uid('m1')}`);
    const raw = JSON.stringify(check.data || {});
    t.eq('Xem trước thấy trùng slot cá nhân', [check.data && check.data.isBusy, (check.data && check.data.conflicts && check.data.conflicts[0] || {}).source], [true, 'PersonalSchedule']);
    t.check('Không lộ tên dự án và tiêu đề việc cá nhân', !raw.includes('Bí mật') && !raw.includes('riêng tư'));
    t.code('Giao việc trùng slot cá nhân → 409', await t.call('leader', 'PUT', `/api/group-tasks/${meeting.id}/assignee`, { assigneeId: t.uid('m1') }), 409, 'GROUP_ASSIGNEE_BUSY');

    // Phiên cá nhân đang chạy chặn bấm giờ việc nhóm
    const own = await t.task('leader', g.id, { title: 'Việc nhóm của m1', assigneeId: t.uid('m1') });
    const running = await t.call('m1', 'POST', '/api/focus/start', { taskId: personal.id });
    t.status('m1 bắt đầu phiên cá nhân → 201', running, 201);
    if (running.data && running.data.sessionId) t.cleanups.push(() => t.call('m1', 'POST', `/api/focus/${running.data.sessionId}/stop`, { stopReason: 'Interrupted' }));
    const blocked = await t.start('m1', own.id);
    t.code('Đang chạy việc cá nhân: Start việc nhóm → 409', blocked, 409, 'FOCUS_SESSION_CONFLICT');
    t.eq('409 chỉ rõ phiên cá nhân', blocked.data && blocked.data.details && blocked.data.details.source, 'Personal');
  });

  // ---------------------------------------------------------------- Chạy
  async function run(env, scenario, onLine) {
    const t = new T(env, scenario);
    t.onLine = onLine;
    const missing = scenario.needs.filter((k) => !env.has(k));
    if (missing.length) {
      return { skipped: true, missing, lines: [], pass: 0, fail: 0 };
    }
    try {
      await scenario.run(t);
    } catch (e) {
      t.check('Kịch bản dừng giữa chừng: ' + e.message, false);
    } finally {
      for (const undo of t.cleanups.reverse()) { try { await undo(); } catch (e) { /* bỏ qua */ } }
      for (const id of t.groups) {
        for (const key of [t.archiveAs, 'leader', 'm1']) {
          try { const r = await env.call(key, 'POST', `/api/groups/${id}/archive`); if (r.ok || r.status === 400) break; } catch (e) { /* bỏ qua */ }
        }
      }
    }
    const pass = t.lines.filter((l) => l.ok).length;
    return { skipped: false, lines: t.lines, pass, fail: t.lines.length - pass };
  }

  const api = { scenarios, run, LABEL };
  if (typeof module !== 'undefined' && module.exports) module.exports = api; else root.GroupScenarios = api;
})(typeof window !== 'undefined' ? window : globalThis);
